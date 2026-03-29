using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Windows.Forms;

using Microsoft.CSharp;

using PRoCon.Core;
using PRoCon.Core.Battlemap;
using PRoCon.Core.Maps;
using PRoCon.Core.Players;
using PRoCon.Core.Players.Items;
using PRoCon.Core.Plugin;
using PRoCon.Core.Plugin.Commands;

using CapturableEvent = PRoCon.Core.Events.CapturableEvents;
//Aliases
using EventType = PRoCon.Core.Events.EventType;

namespace PRoConEvents
{
    public partial class InsaneLimits
    {
        public String GetPluginName()
        {
            return "Insane Limits";
        }

        public String GetPluginVersion()
        {
            return "0.9.18.2";
        }

        public String GetPluginAuthor()
        {
            return "micovery";
        }

        public String GetPluginWebsite()
        {
            return "www.insanegamersasylum.com";
        }


        public String GetPluginDescription()
        {
            return @"... and other contributors.
        <h2>Description</h2>
        This plugin is a customizable limits/rules enforcer. It allows you to setup and enforce limits based on player statistics, and server state. <br />
        <br />
        It tracks extensive Battlelog stats and round stats. Several limits are available in the <a href='http://www.phogue.net/forumvb/forumdisplay.php?36-Plugin-Enhancements'>Procon Plugin Enhancements forum</a>.<br />
        <br />
        Version 0.9.4.0 and later is optionally integrated with a MySQL server (using the <b>Battlelog Cache</b> plugin). This enables caching of Battlelog stats fetching, which
        over time should reduce the delay caused by restarting Procon/enabling Insane Limits when your server is full. This should also reduce load on Battlelog, which in turn will reduce the number of Web errors and exceptions. This version is compatible with TrueBalancer and other plugins that use stats fetching.<br />
        <br />
        In addition to keeping track of player statistics, the plugin also keeps tracks of the number of times a player has activated a certain limit/rule.
        I got this idea from the ProCon Rulz plugin. With this meta-information about limits, you are able to create much more powerful rules such as Spree messages.
        If it's not clear now, it's ok, look at end of the documentation for examples that make use if this information.<br />
        <br />
        By default, the plugin ships with <b>virtual_mode</b> set to <i>True</i>. This allows you to test your limits/rules without any risk of accidentally kicking or banning anyone. Once you feel your limits/rules are ready, you can disable <b>virtual_mode</b>.<br />
        <h2> Minimum Requirements</h2>
        <br />
        This plugin requires you to have sufficient privileges for running the following commands:<br />
        <br />
        <blockquote>
          serverInfo<br />
          mapList.list<br />
          mapList.getMapIndices<br />
          admin.listPlayers all<br />
          punkBuster.pb_sv_command pb_sv_plist<br />
          punkBuster.pb_sv_command pb_sv_ban<br />
          punkBuster.pb_sv_command pb_sv_kick<br />
        </blockquote>
        <br />
        Additionaly, you need to have Read+Write file system permission in the following directories: <br />
        <br />
        <blockquote>
          &lt;ProCon&gt;/<br />
          &lt;ProCon&gt;/Plugins/BF3<br />
          &lt;ProCon&gt;/Plugins/BF4<br />
        </blockquote>
        <br />
        <h2>Supported Limit Evaluations</h2>
        <ul>
            <li><b>OnJoin</b> - Limit evaluated when player joins </li>
            <li><b>OnLeave</b> - Limit evaluated when player leaves </li>
            <li><b>OnSpawn</b> - Limit evaluated when player spawns</li>
            <li><b>OnKill</b> - Limit evaluated when makes a kill (team-kills not counted)</li>
            <li><b>OnTeamKill</b> - Limit evaluated when player makes a team-kill</li>
            <li><b>OnDeath</b> - Limit evaluated when player dies (suices not counted)</li>
            <li><b>OnTeamDeath</b> - Limit evaluated when player is team-killed</li>
            <li><b>OnSuicide</b> - Limit evaluated when player commits suicide</li>
            <li><b>OnAnyChat</b> - Limit evaluated when players sends a chat message</li>
            <li><b>OnInterval</b> - (deprecated) Same behavior as <b>OnIntervalPlayers</b></li>
            <li><b>OnIntervalPlayers</b> - Limit evaluated (for all players) every <b>evaluation_interval</b> number of seconds (minimum 10) </li>
            <li><b>OnIntervalServer</b> - Limit evaluated once every <b>evaluation_interval</b> number of seconds (minimum 10)</li>
            <li><b>OnRoundOver</b> - Limit evaluated when round over event is sent</li>
            <li><b>OnRoundStart</b> - Limit evaluated after round over event, when first player spawns</li>
            <li><b>OnTeamChange</b> - Limit evaluated after after player switches teams</li>
        </ul>

        Note that limit evaluation is only performed after the plugin has fetched the player stats from Battlelog.
        If a player joins the server, and starts team-killing, there will be a couple of seconds before the plugin catches on. Having said that, this is rare behavior.
        Most of the time, by the time the player spawns for the first time, the plugin would have already fetched the stats.<br />
        <br />
        When you enable the plugin for the first time in a full server, it will take a couple of minutes to fetch all player stats<br />
        <br />
        <br />
        <h2>Architecture</h2>
        When the plugin is enabled, it starts two threads:

            <ol>
              <li>
                The <b>fetch</b> thread is in charge of monitoring the players that join the server. It fetches player statistics from battlelog.battlefield.com<br />
              </li>
              <br />
              <li>
               The <b>enforcer</b> thread is in charge of evaluating Interval limits. When the <b>enforcer</b> thread finds that a player violates a limit, it performs an action (Kick, Ban, etc) against that player.<br />
              </li>
            </ol>
            <br />
            The two threads have different responsibilities, but they synchronize their work.<br />
        <br />
        <h2>Fetch-thread Flow</h2>
        <blockquote>
            When players join the server, they are added the stats queue. The fetch thread is constantly monitoring this queue. If there is a player in the queue,
            it removes him from the queue, and fetches the battlelog stats for the player.<br />
            <br />
            The stats queue can grow or shrink depending on how fast players join, and how Int64 the web-requests take. If you enable the plugin on a full server, you will
            see that almost immediately all players are queued for stats fetching. Once the stats are fetched for all players in the queue, they are added to the internal player's list.<br />
            <br />
        </blockquote>
       <br />
       <h2>Enforcer-thread Flow</h2>
        <blockquote>
            The enforcer thread runs on a timer (every second). It checks if there are any interval limits ready to be executed. If there are, it will evaluate those limits.
            <br />
            Each time around that the <b>enforcer</b> checks for the available limits is called an <i>iteration</i>.
            If there are no players in the server, or there are no limits available, the <b>enforcer</b> skips the current <i>iteration</i> and sleeps until the next <i>iteration</i>.<br />
            <br />
            The enforcer is only responsible for Limits that evaluate OnIterval, events. Enforcing for other types of events like OnKill, and OnSpawn, is done in the main thread when procon sends the event information. <br />
        </blockquote>
        <br />
        <h2>Limit Management</h2>
        <blockquote>
            <u>Creation</u> - In order to create a new limit, you have to set <b>new_limit</b> variable to <i>True</i>.<br />
            <br />
            This creates a new limit section with default values that you can change.<br />
            <br />
            <br />
            <u>Deletion</u> - In order to delete a limit, you have to set the variable <b>delete_limit</b> to the numerical <i>id</i> of the limit you want to delete.<br />
            <br />
            Each limit has an <i>id</i> number, you can see the <i>id</i> number in the limit name, e.g. Limit #<b>5</b>.<br />
            <br />
        </blockquote>
        <br />
        <h2>Limit Definition</h2>
        At its basic, there are four fields that determine the structure of a limit. These fields are <b>state</b>, <b>action</b>, and <b>first_check</b>, and <b>second_check</b>.<br />
        <br />
        <ol>
          <li><blockquote><b>state</b><br />
                <i>Enabled</i> - the limit will be used, and actions will be performed live<br />
                <i>Virtual</i> - the limit will be used, but actions will be done in <b>virtual_mode</b><br />
                <i>Disabled</i> - the limit will be ignored<br />
                <br />
                This field is useful if you want to temporarily disable a limit from being used, but still want to preserve its definition.
                <br />
              </blockquote>
          </li>
          <li><blockquote><b>action</b><br />
                <i>(String, psv)</i> - list of actions for this limit (Pipe separated ""|"")<br />
                <br />
                e.g.    Say | PBBan | Mail <br />
                <br />
                These are all the allowed actions:<br />
                <ul>
                <li><i>None</i> - no action is performed against the player<br /></li>
                <li><i>Kick</i> - player is kicked, if the limit evaluates to <i>True</i><br /></li>
                <li><i>EABan</i> - player is banned (using the BF3 ban-list), if the limit evaluates <i>True</i><br /></li>
                <li><i>PBBan</i> - player is banned (using PunkBuster ban-list), if the limit evaluates <i>True</i><br /></li>
                <li><i>Kill</i> - kills the player (delay optional), if the limit evaluates <i>True</i><br /></li>
                <li><i>Say</i> - sends a message the server (All, Team, Squad, or Player), if the limit evaluates <i>True</i><br /></li>
                <li><i>Log</i> - logs a message to a File, Plugin log, or both, if the limit evaluates <i>True</i><br /></li>
                <li><i>Mail</i> - sends an e-mail to specified address, if the limit evaluates <i>True</i><br /></li>
                <li><i>SMS</i> - sends an SMS message to the specified phone number, if the limit evaluates <i>True</i><br /></li>
                <li><i>Tweet</i> - posts a Twitter status update (default account is @InsaneLimits), if the limit evaluates <i>True</i><br /></li>
                <li><i>PBCommand</i> - executes the specified PunkBuster command, if the limit evaluates <i>True</i><br /></li>
                <li><i>ServerCommand</i> - executes the specified Server command, if the limit evaluates <i>True</i><br /></li>
                <li><i>PRoConChat</i> - sends the specified text to PRoCon's Chat-Tab, if the limit evaluates <i>True</i><br /></li>
                <li><i>PRoConEvent</i> - adds the specified event to PRoCon's Events-Tab, if the limit evaluates <i>True</i><br /></li>
                <li><i>TaskbarNotify</i> - sends a Windows Taskbar notification, if the limit evaluates <i>True</i><br /></li>
                <li><i>SoundNotify</i> - plays a sound notification with the specified sound file, if the limit evaluates <i>True</i><br /></li>
                <li><i>Yell</i> - yells a message to the server (All, Team, or Player), if the limit evaluates <i>True</i><br /></li>
                </ul>
                <br />
                <br />
                Depending on the selected action, other fields are shown to specify more information about the action.<br />
                <br />
             </blockquote>
             <br />
             Supported PB ban-duration: <i>Permanent</i>, <i>Temporary</i><br />
             Supported PB ban-type: <i>PB_GUID</i> (default)<br />
             <br />
             Supported EA ban-duration: <i>Permanent</i>, <i>Temporary</i>, <i>Round</i><br />
             Supported EA ban-type: <i>EA_GUID</i>, <i>IPAddress</i>, <i>Name</i><br />
             <br />
             <br />
             Also note that each of these actions have a <b>target</b> player. You have to be careful on what <b>target</b> is for each action.<br />
             <br />
             For example, during a Kill event, the target of the action is the Killer.<br />
             But, during a Death event, the target of the action is the player that was killed <br />
             You don't want to accidentaly Kick/Ban the wrong player!
          </li>
          <li><blockquote><b>first_check</b><br />
                <i>Disabled</i> - the limit does not evaluate anything in the first step of evaluation<br />
                <i>Expression</i> - the limit uses a C# conditional expression during the first step of evaluation<br />
                <i>Code</i> - the limit uses a C# code snippet (must return true/false) during the first step of evaluation<br />
                <br />
              </blockquote>
              <blockquote><b>second_check</b><br />
                <i>Disabled</i> - the limit does not evaluate anything in the second step of evaluation<br />
                <i>Expression</i> - the limit uses a C# conditional expression during the second step of evaluation<br />
                <i>Code</i> - the limit uses a C# code snippet (must return true/false) during the second step of evaluation<br />
                <br />
              </blockquote>
              <br />
              Depending on the selected check type, an extra field will be shown for specifying the <i>Expression</i>, or <i>Code</i> text.<br />
              <br />
              Both <i>Expressions</i>, and <i>Code</i> snippets must be syntactically correct in accordance to the C# language.
              The plugin compiles your <i>Expression</i><i>/</i><i>Code</i> in-memory with the Microsoft C# Compiler.
              If there are compilation errors, those are shown in the plugin log.<br />
              <br />
              If you do not know what C# is, or what an expression is, or what a code snippet is ... do not worry.
              Study the examples in the <a href=""http://www.phogue.net/forumvb/showthread.php?3448-Insane-Limits-Examples&highlight=Insane+Limits"">Examples Index</a> forum thread. Then, if you are still unclear, how to write an expression or a code snippet, ask for help in forums at <a href=""http://phogue.net"">phogue.net</a>
              <br />
          </li>
         </ol>
        <br />
        <h2>Limit Evaluation</h2>
        After compilation, limit evaluation is by far the most important of all steps this plugin goes through.<br />
        <br />
        Limit evaluation is comprised of three steps:<br />
        <br />

        <ol>
        <li><b>first_check</b> Evaluation<br />
        <br />
        During this step, the plugin executes the <i>Expression</i><i>/</i></i>Code</i> in <b>first_check</b> to get a  <i>True</i> or <i>False</i> result.<br />
        <br />
        If the result is <i>False</i>, the plugin does not perform any <b>action</b>, and quits. But, if it's <i>True</i>, it keeps going to the next step <br />
        <br />
        </li>
        <li><b>second_check</b> Evaluation (optional)<br />
        <br />
        Next, the plugin runs the <i>Expression</i><i>/</i></i>Code</i> for the <b>second_check</b>, if it's enabled. If it's not enabled, it keeps going to next step.</br >
        <br />
        </li>
        <li><b>action</b> Execution <br />
        <br />
        If the final result of the limit evaluation is <i>True</i>, the plugin then executes the <b>action</b> associated with the limit.<br />
        <br />
        If the final result of the limit evaluation is <i>False</i>, no <b>action</b> is executed.
        <br />
        </li>
        </ol>

        <h2>Objects</h2>
        When writing a limit <i>Expression</i> or <i>Code</i> snippet, there are several globally defined objects that can be used.
        These are <b>server</b>, <b>player</b>, <b>killer</b>, <b>victim</b>, <b>kill</b>, <b> plugin</b>, <b>team1</b>, <b>team2</b>, <b>team3</b>, <b>team3</b>, and <b>limit</b>. These objects contain values, and functions that can be accessed from within the <i>Expressions</i>, or <i>Code</i> snippets.<br />
        <br />

       <h2>Limit Object</h2>
       The <b>limit</b> Object represents the state the limit that was just activated. This Object is only available during the <b>second_check</b>. The <b>limit</b> Object implements the following interface:<br />
       <br />
<pre>
public interface LimitInfoInterface
{

    //Number of times the limit has been activated, (Current round)
    Double Activations(String PlayerName);
    Double Activations(Int32 TeamId, Int32 SquadId);
    Double Activations(Int32 TeamId);

    // Number of times player has activated this limit (Current round) in the given TimeSpan, e.g. last 10 seconds, etc
    Double Activations(String PlayerName, TimeSpan time);
    Double Activations();


    //Number of times the limit has been activated (All rounds)
    Double ActivationsTotal(String PlayerName);
    Double ActivationsTotal(Int32 TeamId, Int32 SquadId);
    Double ActivationsTotal(Int32 TeamId);
    Double ActivationsTotal();

    // Number of times this limit has been activated by player
    /*
     * Kill, TeamKill: Spree value is reset when player dies
     * Death, TeamDeath, and Suicide: Spree value is reset whe player makes a kill
     *
     * Spawn, Join, Interval: Spree value is never reset, you may reset it manually.
     */

    Double Spree(String PlayerName);


    // manually resets the Spree value for the player, (only for power-users)
    void ResetSpree(String PlayerName);

    /* Data Repository set/get custom data */

    DataDictionaryInterface Data { get; }        //this dictionary is user-managed
    DataDictionaryInterface RoundData { get; }   //this dictionary is automatically cleared OnRoundStart

    /* Other methods */
    String LogFile { get; }

}
</pre>
       <h2>Team Object (team1, team2, team3, team4)</h2>
       The <b>teamX</b> Object represents the state of the team with id X at the moment that the limit is being evaluated. The <b>teamX</b> Object implements the following interface:<br />
       <br />
    <pre>
public interface TeamInfoInterface
{
    List&lt;PlayerInfoInterface&gt; players { get; }

    Double KillsRound { get; }
    Double DeathsRound { get; }
    Double SuicidesRound { get; }
    Double TeamKillsRound { get; }
    Double TeamDeathsRound { get; }
    Double HeadshotsRound { get; }
    Double ScoreRound { get; }

    Int32 TeamId { get; }
    Double Tickets { get; }
    Double RemainTickets { get; }
    Double RemainTicketsPercent { get; }
    Double StartTickets { get; }

    // BF4
    Int32 Faction { get; } // US = 0, RU = 1, CN = 2
}
    </pre>

       <h2>Server Object</h2>
       The <b>server</b> Object represents the state of the server at the moment that the limit is being evaluated. The <b>server</b> Object implements the following interface:<br />
       <br />
    <pre>
public interface ServerInfoInterface
{
    /* Server State */
    Int32 CurrentRound { get; }
    Int32 TotalRounds { get; }
    Int32 PlayerCount { get; }
    Int32 MaxPlayers { get; }

    /* Current Map Data */
    Int32 MapIndex { get; }
    String MapFileName { get; }
    String Gamemode { get; }
    Double GameModeCounter { get; }
    Double CTFRoundTimeModifier { get; }

    /* Next Map Data */
    Int32 NextMapIndex { get; }
    String NextMapFileName { get; }
    String NextGamemode { get; }

    /* Map Rotation */
    List&lt;String&gt; MapFileNameRotation { get; }
    List&lt;String&gt; GamemodeRotation { get; }
    List&lt;Int32&gt; LevelRoundsRotation { get; }

    /* All players, Current Round, Stats */
    Double KillsRound { get; }
    Double DeathsRound { get; }   // kind of useless, should be same as KillsTotal (suices not counted as death)
    Double HeadshotsRound { get; }
    Double SuicidesRound { get; }
    Double TeamKillsRound { get; }


    /* All players, All rounds, Stats */

    Double KillsTotal { get; }
    Double DeathsTotal { get; }  // kind of useless, should be same s KillsTotal (suicides not counted as death)
    Double HeadshotsTotal { get; }
    Double SuicidesTotal { get; }
    Double TeamKillsTotal { get; }


    /* Weapon Stats, Current Round, All Rounds (Total)*/
    WeaponStatsInterface this[String WeaponName] { get; }

    /* Other data */
    Double TimeRound { get; }                // Time since round started
    Double TimeTotal { get; }                // Time since plugin enabled
    Double TimeUp { get; }                   // Time since last server restart
    Double RoundsTotal { get; }              //Round played since plugin enabled

    /* Meta Data */
    String Port { get; }                     // Layer/Server port number
    String Host { get; }                     // Layer/Server Host
    String Name { get; }
    String Description { get; }
    String GameVersion { get; } // BF3 or BF4

    /* var.* value that is updated every update_interval seconds */
    Int32 BulletDamage { get; }
    Boolean FriendlyFire { get; }
    Int32 GunMasterWeaponsPreset { get; }
    Double IdleTimeout { get; } // seconds
    Int32 SoldierHealth { get; }
    Boolean VehicleSpawnAllowed { get; }
    Int32 VehicleSpawnDelay { get; }
    // BF4
    Boolean Commander { get; }
    Int32 MaxSpectators { get; }
    String ServerType { get; }
    Int32 GetFaction(Int32 TeamId); // US = 0, RU = 1, CN = 2

    /* Team data */
    Double Tickets(Int32 TeamId);              //tickets for the specified team
    Double RemainTickets(Int32 TeamId);        //tickets remaining on specified team
    Double RemainTicketsPercent(Int32 TeamId); //tickets remaining on specified team (as percent)

    Double StartTickets(Int32 TeamId);         //tickets at the begining of round for specified team
    Double TargetTickets { get; }            //tickets needed to win

    Int32 OppositeTeamId(Int32 TeamId);          //id of the opposite team, 1->2, 2->1, 3->4, 4->3, *->0
    Int32 WinTeamId { get; }                   //id of the team that won previous round

    /* Data Repository set/get custom data */

    DataDictionaryInterface Data { get; }        //this dictionary is user-managed
    DataDictionaryInterface RoundData { get; }   //this dictionary is automatically cleared OnRoundStart

}
    </pre>
       <h2>Kill Object</h2>
       The <b>kill</b> Object represents information about the kill event. The <b>kill</b> Object implements the following interface:<br />
       <br />
<pre>
public interface KillInfoInterface
{
    String Weapon { get; }
    Boolean Headshot { get; }
    DateTime Time { get; }
    String Category { get; } // BF3.defs or BF4.defs weapon category, such as SniperRifle or VehicleAir
}
</pre>
       <br />
       The <b>KillReasonInterface</b> Object represents the friendly weapon name, broken down into separate fields:<br />
       <br />
<pre>
public interface KillReasonInterface
{
    String Name { get; } // weapon name or reason, like Suicide
    String Detail { get; } // BF4: ammo or attachment
    String AttachedTo { get; } // BF4: main weapon when Name is a secondary attachment, like M320
    String VehicleName { get; } // BF4: if Name is Death, this is the vehicle's name
    String VehicleDetail { get; } // BF4: if Name is Death, this is the vehicle's detail (stuff after final slash)
}
</pre>
       <h2>Player, Killer, Victim Objects</h2>
       The <b>player</b> Object represents the state of player for which the current limit is being evaluated. The <b>player</b> Object implements the following interface:<br />
       <br />
<pre>
public interface PlayerInfoInterface
{
    /* Online statistics (battlelog.battlefield.com) */
    Double Rank { get; }
    Double Kdr { get; }
    Double Time { get; }
    Double Kills { get; }
    Double Wins { get; }
    Double Skill { get; }
    Double Spm { get; }
    Double Score { get; }
    Double Deaths { get; }
    Double Losses { get; }
    Double Repairs { get; }
    Double Revives { get; }
    Double Accuracy { get; }
    Double Ressuplies { get; }
    Double QuitPercent { get; }
    Double ScoreTeam { get; }
    Double ScoreCombat{ get; }
    Double ScoreVehicle{ get; }
    Double ScoreObjective { get; }
    Double VehiclesKilled { get; }
    Double KillStreakBonus { get; }
    Double Kpm { get; }

    Double KillAssists { get; }
    Double ResetDeaths { get; }
    Double ResetKills { get; }
    Double ResetLosses { get; }
    Double ResetWins { get; }
    Double ResetScore { get; }
    Double ResetShotsFired { get; }
    Double ResetShotsHit { get; }
    Double ResetTime { get; }

    Double ReconTime { get; }
    Double EngineerTime { get; }
    Double AssaultTime { get; }
    Double SupportTime { get; }
    Double VehicleTime { get; }
    Double ReconPercent { get; }
    Double EngineerPercent { get; }
    Double AssaultPercent { get; }
    Double SupportPercent { get; }
    Double VehiclePercent { get; }

    /* Player data */
    String Name { get; }
    String FullName { get; } // name including clan-tag
    String Tag { get; }
    String IPAddress { get; }
    String CountryCode { get ; }
    String CountryName { get; }
    String PBGuid { get; }
    String EAGuid { get; }
    Int32 TeamId { get; }
    Int32 SquadId { get; }
    Int32 Ping { get; }
    Int32 MaxPing { get; }
    Int32 MinPing { get; }
    Int32 MedianPing { get; } // of the last five samples
    Int32 AveragePing { get; } // of two to five samples
    Int32 Role { get; } // BF4: 0 = PLAYER, 1 = SPECTATOR, 2 = COMMANDER, 3 = MOBILE COMMANDER


    /* Current round, Player Stats */
    Double KdrRound { get; }
    Double KpmRound { get; }
    Double SpmRound { get; }
    Double ScoreRound { get; }
    Double KillsRound { get; }
    Double DeathsRound { get; }
    Double HeadshotsRound { get; }
    Double TeamKillsRound { get; }
    Double TeamDeathsRound { get; }
    Double SuicidesRound { get; }
    Double TimeRound { get; }

    /* All rounds, Player Stats */
    Double KdrTotal { get; }
    Double KpmTotal { get; }
    Double SpmTotal { get; }
    Double ScoreTotal { get; }
    Double KillsTotal { get; }
    Double DeathsTotal { get; }
    Double HeadshotsTotal { get; }
    Double TeamKillsTotal { get; }
    Double TeamDeathsTotal { get; }
    Double SuicidesTotal { get; }
    Double TimeTotal { get; }
    Double RoundsTotal { get; }

    /* Weapon Stats, Current Round, All Rounds (Total) */
    WeaponStatsInterface this[String WeaponName] { get; }

    /* Battlelog Weapon Stats function: use kill.Weapon for WeaponName */
    BattlelogWeaponStatsInterface GetBattlelog(String WeaponName);

    /* Other Data */
    DateTime JoinTime { get; }
    String LastChat { get; }   // text of the last chat sent by player
    Boolean Battlelog404 { get; } // True - Player has PC Battlelog profile
    Boolean StatsError { get; }   // True - Error occurred while processing player stats


    /* Whitelist information */
    Boolean inClanWhitelist { get; }
    Boolean inPlayerWhitelist { get; }
    Boolean isInWhitelist { get; }


    /* Data Repository set/get custom data */

    DataDictionaryInterface Data { get; }        //this dictionary is user-managed
    DataDictionaryInterface RoundData { get; }   //this dictionary is automatically cleared OnRoundStart
}
</pre>

       <h2>Plugin Object</h2>
       The <b>plugin</b> represents this plugin itself. It gives you access to important functions for executing server commands, and interacting with ProCon.
       The <b>plugin</b> Object implements the following interface:<br />
       <br />
<pre>
public interface PluginInterface
{

    /*
     * Methods for sending messages
     */
    Boolean SendGlobalMessage(String message);
    Boolean SendTeamMessage(Int32 teamId, String message);
    Boolean SendSquadMessage(Int32 teamId, Int32 squadId, String message);
    Boolean SendPlayerMessage(String name, String message);

    Boolean SendGlobalMessage(String message, Int32 delay);
    Boolean SendTeamMessage(Int32 teamId, String message, Int32 delay);
    Boolean SendSquadMessage(Int32 teamId, Int32 squadId, String message, Int32 delay);
    Boolean SendPlayerMessage(String name, String message, Int32 delay);

    Boolean SendGlobalYell(String message, Int32 duration);
    Boolean SendTeamYell(Int32 teamId, String message, Int32 duration);
    Boolean SendPlayerYell(String name, String message, Int32 duration);

    Boolean SendMail(String address, String subject, String body);
    Boolean SendSMS(String country, String carrier, String number, String message);

    /*
     * Methods used for writing to the Plugin console
     */
    void ConsoleWrite(String text);
    void ConsoleWarn(String text);
    void ConsoleError(String text);
    void ConsoleException(String text);

    /*
     * Methods for getting whitelist information
     *
     */
    Boolean isInWhitelist(String PlayerName);
    Boolean isInPlayerWhitelist(String PlayerName);
    Boolean isInClanWhitelist(String PlayerName);
    Boolean isInWhiteList(String PlayerName, String list_name);

    /* Method for checking generic lists */
    Boolean isInList(String item, String list_name);
    
    List&lt;String&gt; GetReservedSlotsList();

    /*
     * Methods getting and setting the Plugin's variables
     */
    Boolean setPluginVarValue(String variable, String value);
    String getPluginVarValue(String variable);


    /*
     *  Method: R
     *
     *  Replaces tags like %p_n% (Player Name), %k_n% (Killer Name), %v_n% (Victim Name), etc
     */
    String R(String message);


    /*
     * Methods for actions
     */

    Boolean KickPlayerWithMessage(String name, String message);
    Boolean KillPlayer(String name);  /* deprecated, do not use */
    Boolean KillPlayer(String name, Int32 delay);
    Boolean EABanPlayerWithMessage(EABanType type, EABanDuration duration, String name, Int32 minutes, String message);
    Boolean PBBanPlayerWithMessage(PBBanDuration duration, String name, Int32 minutes, String message);
    Boolean PBCommand(String text);
    Boolean MovePlayer(String name, Int32 TeamId, Int32 SquadId, Boolean force);
    Boolean SendTaskbarNotification(String title, String messagge);
    Boolean Log(String file, String message);
    Boolean Tweet(String status);
    Boolean PRoConChat(String text);
    Boolean PRoConEvent(String text, String player);
    Boolean SendSoundNotification(String soundfile, String soundfilerepeat);

    void ServerCommand(params String[] arguments);

    /*
     * Examples:
     *
     *           KickPlayerWithMessage(""micovery"" , ""Kicked you for team-killing!"");
     *           EABanPlayerWithMessage(EABanType.EA_GUID, EABanDuration.Temporary, ""micovery"", 10, ""You are banned for 10 minutes!"");
     *           PBBanPlayerWithMessage(PBBanDuration.Permanent, ""micovery"", 0, ""You are banned forever!"");
     *           ServerCommand(""admin.listPlayers"", ""all"");
     */


    /* Other Methods */
    String FriendlySpan(TimeSpan span);         //converts a TimeSpan into a friendly formatted String e.g. ""2 hours, 20 minutes, 15 seconds""
    String BestPlayerMatch(String name);        //looks in the internal player's list, and finds the best match for the given player name

    Boolean IsCommand(String text);                //checks if the given text start with one of these characters: !/@?
    String ExtractCommand(String text);         //if given text starts with one of these charactets !/@? it removes them
    String ExtractCommandPrefix(String text);   //if given text starts with one of these chracters !/@? it returns the character
    
    /* This method checks if the currently in-game player with matching name has
       a Procon account on the Procon instance controlling this game server. Returns
       False if the name does not match any of the players currently joined to the game server.
       The canBan value is set to true if the player can temporary ban or permanently ban.
    */
    Boolean CheckAccount(String name, out Boolean canKill, out Boolean canKick, out Boolean canBan, out Boolean canMove, out Boolean canChangeLevel);

    Double CheckPlayerIdle(String name); // -1 if unknown, otherwise idle time in seconds

    /* Updated every update_interval seconds */
    Boolean IsSquadLocked(Int32 teamId, Int32 squadId); // False if unknown or open, True if locked
    String GetSquadLeaderName(Int32 teamId, Int32 squadId); // null if unknown, player name otherwise

    /* This method looks in the internal player's list for player with matching name.
     * If fuzzy argument is set to true, it will find the player name that best matches the given name
     *
     /
    PlayerInfoInterface GetPlayer(String name, Boolean fuzzy);

    /*
     * Creates a file in ProCon's directory  (InsaneLimits.dump)
     * Detailed information about the exception.
     */
    void DumpException(Exception e);

    /* Data Repository set/get custom data */

    DataDictionaryInterface Data { get; }        //this dictionary is user-managed
    DataDictionaryInterface RoundData { get; }   //this dictionary is automatically cleared OnRoundStart

    /* Friendly names */
    String FriendlyMapName(String mapFileName);  //example: ""MP_001"" -> ""Grand Bazaar""
    String FriendlyModeName(String modeName);    //example: ""TeamDeathMatch0"" -> ""TDM""
    KillReasonInterface FriendlyWeaponName(String killWeapon); 
        // BF3 example: ""Weapons/XP2_L86/L86"" => KillReasonInterface(""L86"", null, null)
        // BF4 example: ""U_AK12_M320_HE"" => KillReasonInterface(""M320"", ""HE"", ""AK12"")
        // BF4 vehicle example: ""Gameplay/Vehicles/AH6/AH6_Littlebird"" => KillReasonInterface(""Death"", null, null, ""AH6"", ""AH6_Littlebird"")

    /* External plugin support */
    Boolean IsOtherPluginEnabled(String className, String methodName);
    void CallOtherPlugin(String className, String methodName, Hashtable parms);
    DateTime GetLastPluginDataUpdate(); // return timestamp for the last time InsaneLimits.UpdatePluginData() was called
}
</pre>

 <h2>Data and Objects</h2>
       The <b>Data</b> Object is a nested dictionary of key/value pairs that you can use to store custom data inside the <b>plugin</b>, <b>server</b>, <b>limit</b>, <b>player</b>, <b>killer</b>, and <b>victim</b> objects. The <b>Data</b> Object implements the following interface:<br />
       <br />
    <pre>
public interface DataDictionaryInterface
{
    /* String Data */
    String setString(String key, String value);
    String getString(String key);
    String unsetString(String key);
    Boolean issetString(String key);
    List<String> getStringKeys();

    /* Boolean Data */
    Boolean setBool(String key, Boolean value);
    Boolean getBool(String key);
    Boolean unsetBool(String key);
    Boolean issetBool(String key);
    List<String> getBoolKeys();

    /* Double Data */
    Double setDouble(String key, Double value);
    Double getDouble(String key);
    Double unsetDouble(String key);
    Boolean issetDouble(String key);
    List<String> getDoubleKeys();

    /* Int Data */
    Int32 setInt(String key, Int32 value);
    Int32 getInt(String key);
    Int32 unsetInt(String key);
    Boolean issetInt(String key);
    List<String> getIntKeys();

    /* Object Data */
    Object setObject(String key, Object value);
    Object getObject(String key);
    Object unsetObject(String key);
    Boolean issetObject(String key);
    List<String> getObjectKeys();

    /* Generic set/get methods */
    Object set(Type type, String key, Object value);
    Object get(Type type, String key);
    Object unset(Type type, String key);
    Boolean isset(Type type, String key);
    List<String> getKeys(Type type);

    /* Other methods */
    void Clear();  /* clear/unset all data from repository */

}
    </pre>

        <h2>Simple (Traditional) Replacements</h2>
        This plugin supports an extensive list of message text replacements. A replacement is a String that starts and ends with the percent character ""%"".
        When you use them in the text of a message, the plugin will try to replace it with the corresponding value. For example: <br />
        <br />
        The message <br />
        <br />
        <pre>
    ""%k_n% killed %v_n% with a %w_n%""
        </pre>
        <br />
        becomes<br />
        <br />
        <pre>
    ""micovery killed NorthEye with a PP-2000""
        </pre>
        <br />
        Below is a list of all the replacements supported. Some replacements are not available for all types of events. For example, Killer-Name replacement is not available for OnSpawn event. <br />
        <br />
        <pre>
    public String[] Replacements = new String[]
    {
        // Killer Replacements (Evaluations:  OnKill, OnDeath, OnTeamKills, and OnTeamDeath)
        /* k   - killer
         * n   - name
         * ct  - Clan-Tag
         * cn  - Country Name
         * cc  - Country Code
         * ip  - IPAddress
         * eg  - EA GUID
         * pg  - Punk Buster GUID
         */
        ""%k_n%"",    ""Killer name"",
        ""%k_ct%"",   ""Killer clan-Tag"",
        ""%k_cn%"",   ""Killer county-name"",
        ""%k_cc%"",   ""Killer county-code"",
        ""%k_ip%"",   ""Killer ip-address"",
        ""%k_eg%"",   ""Killer EA GUID"",
        ""%k_pg%"",   ""Killer Punk-Buster GUID"",
        ""%k_fn%"",   ""Killer full name, includes Clan-Tag (if any)"",

        // Victim Replacements (Evaluations:  OnKill, OnDeath, OnTeamKills, and OnTeamDeath)

        /* Legend:
         * v   - victim
         */
        ""%v_n%"",    ""Victim name"",
        ""%v_ct%"",   ""Victim clan-Tag"",
        ""%v_cn%"",   ""Victim county-name"",
        ""%v_cc%"",   ""Victim county-code"",
        ""%v_ip%"",   ""Victim ip-address"",
        ""%v_eg%"",   ""Victim EA GUID"",
        ""%v_pg%"",   ""Vitim Punk-Buster GUID"",
        ""%v_fn%"",   ""Victim full name, includes Clan-Tag (if any)"",

        // Player Repalcements (Evaluations: OnJoin, OnLeave, OnSpawn, OnTeamChange, OnAnyChat, and OnSuicide)

        /* Legend:
         * p   - player
         * lc  - last chat
         */
        ""%p_n%"",    ""Player name"",
        ""%p_ct%"",   ""Player clan-Tag"",
        ""%p_cn%"",   ""Player county-name"",
        ""%p_cc%"",   ""Player county-code"",
        ""%p_ip%"",   ""Player ip-address"",
        ""%p_eg%"",   ""Player EA GUID"",
        ""%p_pg%"",   ""Player Punk-Buster GUID"",
        ""%p_fn%"",   ""Player full name, includes Clan-Tag (if any)"",
        ""%p_lc%"",   ""Player, Text of last chat"",
        // Weapon Replacements (Evaluations: OnKill, OnDeath, OnTeamKill, OnTeamDeath, OnSuicide)

        /* Legend:
         * w   - weapon
         * n   - name
         * p   - player
         * a   - All (players)
         * x   - count
         */
        ""%w_n%"",    ""Weapon name"",
        ""%w_p_x%"",  ""Weapon, number of times used by player in current round"",
        ""%w_a_x%"",  ""Weapon, number of times used by All players in current round"",

        // Limit Replacements for Activations & Spree Counts (Evaluations: Any)

        /* Legend:
         * th  - ordinal count suffix e.g. 1st, 2nd, 3rd, 4th, etc
         * x   - count, 1, 2, 3, 4, etc
         * p   - player
         * s   - squad
         * t   - team
         * a   - All (players)
         * r   - SpRee
         */
        ""%p_x_th%"",  ""Limit, ordinal number of times limit has been activated by the player"",
        ""%s_x_th%"",  ""Limit, ordinal number of times limit has been activated by the player's squad"",
        ""%t_x_th%"",  ""Limit, ordinal number of times limit has been activated by the player's team"",
        ""%a_x_th%"",  ""Limit, ordinal number of times limit has been activated by all players in the server"",
        ""%r_x_th%"",  ""Limit, ordinal number of times limit has been activated by player without Spree value being reset"",
        ""%p_x%"",     ""Limit, number of times limit has been activated by the player"",
        ""%s_x%"",     ""Limit, number of times limit has been activated by the player's squad"",
        ""%t_x%"",     ""Limit, number of times limit has been activated by the player's team"",
        ""%a_x%"",     ""Limit, number of times limit has been activated by all players in the server"",
        ""%r_x%"",     ""Limit, number of times limit has been activated by player without Spree value being reset"",


        // Limit Replacements for Activations & Spree Counts (Evaluations: Any) ... (All Rounds)
        /* Legend:
         * xa - Total count, for all rounds
         */
        ""%p_xa_th%"",  ""Limit, ordinal number of times limit has been activated by the player"",
        ""%s_xa_th%"",  ""Limit, ordinal number of times limit has been activated by the player's squad"",
        ""%t_xa_th%"",  ""Limit, ordinal number of times limit has been activated by the player's team"",
        ""%a_xa_th%"",  ""Limit, ordinal number of times limit has been activated by all players in the server"",
        ""%p_xa%"",     ""Limit, number of times limit has been activated by the player"",
        ""%s_xa%"",     ""Limit, number of times limit has been activated by the player's squad"",
        ""%t_xa%"",     ""Limit, number of times limit has been activated by the player's team"",
        ""%a_xa%"",     ""Limit, number of times limit has been activated by all players in the server"",


        ""%date%"", ""Current date, e.g. Sunday December 25, 2011"",
        ""%time%"", ""Current time, e.g. 12:00 AM""

        ""%server_host%"", ""Server/Layer host/IP "",
        ""%server_port%"", ""Server/Layer port number""

        ""%l_id%"", ""Limit numeric id"",
        ""%l_n%"",  ""Limit name""

    };
        </pre>
        <h2>Advanced Replacements</h2>
        In addition to the simple %<b>key</b>% replacments, this plugin also allows you to use a more advanced type of replacement. Within strings, you can use replacements that match properties in known objects. For example, if you use <b>player.Name</b> within a String, the plugin will detect it and replace it appropriately.<br />
        <br />
        A common usage for advanced replacements is to list player stats in the Kick/Ban reason. For example:
        <br />
        <br />
        The message <br />
        <br />
        <pre>
    ""player.Name you were banned for suspicious stats: Kpm: player.Kpm, Spm: player.Spm, Kdr: player.Kdr""
        </pre>
        <br />
        becomes<br />
        <br />
        <pre>
    ""micovery you were banned for suspicious stats: Kpm: 0.4, Spm: 120, Kdr: 0.61""
        </pre>
        <br />
        <h2>Settings</h2>
        <ol>
          <li><blockquote><b>use_direct_fetch</b><br />
                <i>True</i> - if the cache is not available, fetch stats directly from Battlelog<br />
                <i>False</i> - disable direct fetches from Battlelog<br />
                <br />
                If the <b>Battlelog Cache</b> plugin is installed, up to date and enabled,
                it will be used for player stats regardless of the setting of this
                option. If the <b>Battlelog Cache</b> plugin
                is not installed, not up to date or disabled, setting
                <b>use_direct_fetch</b> to True will act as a fallback system, fetching
                stats directly from Battlelog. Otherwise, stats fetching will
                fail since the cache is not available and this setting is False.
                </blockquote>
          </li>
          <li><blockquote><b>use_battlelog_proxy</b><br />
                <i>True</i> - Send requests to web services over a proxy.<br />
                <i>False</i> - Do not use a proxy.<br />
                <br />
                </blockquote>
          </li>
          <li><blockquote><b>proxy_url</b><br />
                <i>(String, url)</i> - Format: http://IP:PORT - http://user:password@IP:PORT<br />
                <br />
                The URL of the proxy server.
                </blockquote>
          </li>
          <li><blockquote><b>use_slow_weapon_stats</b><br />
                <i>False</i> - skip fetching weapon stats for new players<br />
                <i>True</i> - fetch weapon stats for new players<br />
                <br />
                Visible only if <b>use_direct_fetch</b> is set to True.
                Fetching weapon stats from Battlelog takes a Int64 time, 15 seconds or more
                per player. By default, this slow fetch is disabled (False), so that
                your Procon restart or initial plugin enable time on a full server
                won't be delayed or bogged down while fetching weapon stats. However,
                if you have limits that use the GetBattlelog() function, you <b>must</b>
                set this value to True, or else stats will not be available.
                Also, see <b>rcon_to_battlelog_codes</b>.
                </blockquote>
          </li>
          <li><blockquote><b>use_stats_log</b><br />
                <i>False</i> - do not log Battlelog stats to the battle.log file<br />
                <i>True</i> - log player stats to the battle.log file<br />
                <br />
                If stats fetching is enabled and stats are fetched successfully, all the stats that were fetched will be logged in a file that follows the standard logging file name pattern: procon/Logs/<server-ip>_<server-port>/YYYYMMDD_battle.log (text file).
                </blockquote>
          </li>
          <li><blockquote><strong>limits_file</strong><br />
                <i>(String, path)</i> - path to the file where limits, and lists are saved
                </blockquote>
           </li>
          <li><blockquote><b>auto_load_interval</b><br />
                <i>(integer >= 60)</i> - interval in seconds, for auto loading settings from the <b>limits_file</b><br />
                <br />
                </blockquote>
          </li>
           <li><blockquote><strong>player_white_list</strong><br />
                <i>(String, csv)</i> - list of players that should never be kicked or banned
                </blockquote>
           </li>
           <li><blockquote><strong>clan_white_list</strong><br />
                <i>(String, csv)</i> - list of clan (tags) for players that should never be kicked or banned
                </blockquote>
           </li>
           <li><blockquote><strong>virtual_mode</strong><br />
                <i>True</i> - limit <b>actions</b> (kick, ban) are simulated, the actual commands are not sent to server <br />
                <i>False</i> - limit <b>actions</b> (kick, ban) are not simulated <br />
            </blockquote>
           </li>
           <li><blockquote><strong>console</strong><br />
                <i>(String)</i> - you can use this field to run plugin commands <br />
                <br />
                For example: ""!stats micovery"" will print the player statistic for the current round in the plugin console. <br />
                <br />
                Note that plugin commands, are currently supported only inside ProCon, and not In-Game.
                </blockquote>
           </li>
          <li><blockquote><b>rcon_to_battlelog_codes</b><br />
                <i>String[] Array</i> - Syntax: RCON=Battlelog, e.g., U_XBOW=WARSAW_ID_P_WNAME_CROSSBOW<br />
                <br />
                Visible only if <b>use_slow_weapon_stats</b> is True.
                Lets you define mappings from RCON weapon codes to Battelog weapon stats codes. Useful when new unlocks or DLC
                are released and in-use before the next update of this plugin is available. You can also override
                incorrect mappings built-in to the plugin, if any.
                </blockquote>
          </li>
          <li><blockquote><b>smtp_port</b><br />
                <i>(String)</i> - Address of the SMTP Mail server used for <i>Mail</i> action<br />
                </blockquote>
          </li>
           <li><blockquote><b>smtp_port</b><br />
                <i>(integer > 0)</i> - port number of the SMTP Mail server used for <i>Mail</i> action<br />
                </blockquote>
          </li>
          <li><blockquote><b>smtp_account</b><br />
                <i>(String)</i> - mail address for authenticating with the SMTP Mail used for <i>Mail</i> action<br />
                </blockquote>
          </li>
          <li><blockquote><b>smtp_mail</b><br />
                <i>(String)</i> - mail address (Sender/From) that is used for sending used for <i>Mail</i> action<br />
                <br />
                This is usually the same as <b>smtp_account</b> ... depends on your SMTP Mail provider.
                </blockquote>
          </li>
          <li><blockquote><b>say_interval</b><br />
                <i>(Single)</i> - interval in seconds between say messages. Default value is 0.05, which is 50 milli-seconds<br />
                <br />
                The point of this setting is to avoid spam, but you should not set this value too large. Ideally it should be between 0 and 1 second.
                </blockquote>
          </li>
          <li><blockquote><b>wait_timeout</b><br />
                <i>(Int32)</i> - interval in seconds to wait for a response from the game server<br />
                <br />
                If you get several <b>Timeout(xx seconds) expired, while waiting for ...</b>
                exceptions in plugin.log, try increasing the wait_timeout value by 10 seconds.
                Repeat until the exceptions stop, but you should not exceed 90 seconds.
                </blockquote>
          </li>
        </ol>
       <br />
       <h2> Plugin Commands</h2>

       These are the commands supported by this plugin. You can run them from within the <b>console</b> field. Replies to the commands are printed in the plugin log.<br />
       <br />
       <ul>
           <li><blockquote>
                <b> !round stats</b><br />
                Aggregate stats for all players, current round<br />
                <br /><br />
                <b> !total stats</b><br />
                Aggregate stats for all players, all rounds<br />
                <br /><br />
                <b> !weapon round stats</b><br />
                Weapon-Level round stats for all players, current round<br />
                <br /><br />
                <b> !weapon total stats</b><br />
                Weapon-Level stats for all players, all rounds<br />
                <br /><br />
                <b> !web stats {player}</b><br />
                Battlelog stats for the current player<br />
                <br /><br />
                <b> !round stats {player}</b><br />
                Aggregate stats for the current player, current round<br />
                <br /><br />
                <b> !total stats {player}</b ><br />
                Aggregate stats for the current player, all rounds<br />
                <br /><br />
                <b> !weapon round stats {player}</b><br />
                Weapon-Level stats for the current player, current round<br />
                <br /><br />
                <b> !weapon total stats {player}</b><br />
                Weapon-Level stats for the current player, all round<br />
               <br />
               <br />
               These are the most awesome of all the commands this plugin provides. Even if you are not using this plugin to enforce any limit, you could have it enabled for just monitoring player stats.<br />
               <br />
               When calling player specific statistic commands, if you misspell, or only type part of the player name, the plugin will try to find the best match for the player name.<br />
               <br />
               </blockquote>
           </li>
           <li><blockquote><b>!dump limit {id}</b><br />
               <br />
               This command creates a file in ProCon's directory containing the source-code for the limit with the specified <i>id</i><br />
               <br />
               For example, the following command <br />
               <br />
               !dump limit <b>5</b><br />
               <br />
                Creates the file ""LimitEvaluator<b>5</b>.cs"" inside ProCon's directory. <br />
                <br />
                This command is very useful for debugging compilation errors, as you can see the code inside the file exactly as the plugin sees it (with the same line and column offsets).
               </blockquote>
           </li>
           <li><blockquote>
                <b> !set {variable} {to|=} {value}</b><br />
                <b> !set {variable} {value}</b><br />
                <b> !set {variable}</b><br />
                <br />
                This command is used for setting the value of this plugin's variables.<br />
                For the last invocation syntax the value is assumed to be ""True"". <br />
               </blockquote>
           </li>
           <li><blockquote>
                <b>!get {variable} </b><br />
                <br />
                This command prints the value of the specified variable.
               </blockquote>
           </li>
       </ul>

      <h2> In-Game Commands</h2>

       These are the In-Game commands supported by this plugin. You can run them only from within the game. Replies to the commands are printed in the game chat.<br />
       <br />
       <ul>
           <li><blockquote>
                <b> !stats</b><br />
                List the available stats, Battlelog<br />
                <br /><br />
                <b> !stats [web|battlelog]</b><br />
                List the available stats, Battlelog<br />
                <br /><br />
                <b> !stats round</b><br />
                List the available stats, current round<br />
                <br /><br />
                <b> !stats total</b><br />
                List the available stats, all rounds<br />
                <br />
                These commands are used as a shortcut for players to view what type of stats they can query. The plugin will try to fit all stat types into a single chat message.<br />
                <br />
               </blockquote>
            </li>
           <li><blockquote>
                <b> !my {type}</b><br />
                Print Battlelog stat of the specified <b>type</b> for the player that executed the command<br />
                <br />
                <b> !my round {type}</b><br />
                Print current round stat of the specified <b>type</b> for the player that executed the command<br />
                <br />
                <b> !my total {type}</b><br />
                Print all rounds stat of the specified <b>type</b> for the player that executed the command<br />
                <br />
                <b> ?{player} {type}</b><br />
                Print Battlelog stat of the specified <b>type</b> for the specified <b>player</b><br />
                <br />
                <b> ?{player} round {type}</b><br />
                Print current round stat of the specified <b>type</b> for the specified <b>player</b><br />
                <br />
                <b> ?{player} total {type}</b><br />
                Print all rounds stat of the specified <b>type</b> for the specified <b>player</b><br />
                <br />
                <br />
                The <b>player</b> name can be a sub-String, or even misspelled. The plugin will find the best match.<br />
                <br />
               </blockquote>
           </li>
       </ul>
       <blockquote>
       Annex 1 - Boolean Operators: <br />
       <br />
       For combining <i>Expressions</i> you use <i>Boolean Logic</i> operators. These are: <br />
       <br />

       <ul>
              <li>AND (Conjunction): <b>&&</b></li>
              <li>OR  (Disjunction): <b>||</b></li>
              <li>NOT (Negation): <b>!</b></li>
       </ul>
       </blockquote>
       <blockquote>
       Annex 2 - Relational Operators: <br />
       <br />
       All the previous examples use the Greater-Than ( <b>&gt;</b> ) operator a lot, but that is not the only relational operator supported. These are the arithmetic relational operators you can use:<br />
       <br />
       <ul>
              <li>Greater-Than: <b>&gt;</b></li>
              <li>Greater-than-or-Equal: <b>&gt;=</b></li>
              <li>Less-than: <b>&lt;</b></li>
              <li>Less-than-or-Equal: <b>&lt;=</b></li>
              <li>Equality: <b>==</b></li>
              <li>Not-Equal: <b>!=</b></li>
       </ul>
       <br />
        ";
        }
    }
