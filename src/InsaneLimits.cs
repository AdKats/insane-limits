/*
 * Copyright 2011 Miguel Mendoza - miguel@micovery.com, PapaCharlie9, Singh400, EBastard, Hedius
 *
 * Insane Balancer is free software: you can redistribute it and/or modify it under the terms of the
 * GNU General Public License as published by the Free Software Foundation, either version 3 of the License,
 * or (at your option) any later version. Insane Balancer is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
 * See the GNU General Public License for more details. You should have received a copy of the
 * GNU General Public License along with Insane Balancer. If not, see http://www.gnu.org/licenses/.
 *
 */

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
    // bitwise safe enums

    public enum HTTPMethod
    {
        POST = 0x01,
        GET = 0x02,
        PUT = 0x04
    };

    public enum EABanDuration
    {
        Temporary = 0x01,
        Permanent = 0x02,
        Round = 0x04
    };

    public enum PBBanDuration
    {
        Temporary = 0x01,
        Permanent = 0x02
    };

    public enum MessageAudience
    {
        All = 0x01,
        Team = 0x02,
        Squad = 0x04,
        Player = 0x08
    };

    public enum EABanType
    {
        EA_GUID = 0x01,
        IPAddress = 0x02,
        Name = 0x04
    };

    public enum PBBanType { PB_GUID = 0x01 };

    public enum StatSource
    {
        Web = 0x01,
        Round = 0x02,
        Total = 0x04
    };

    public enum BaseEvent
    {
        None = 0x000,
        Kill = 0x001,
        Suicide = 0x002,
        TeamKill = 0x004,
        Spawn = 0x008,
        GlobalChat = 0x010,
        TeamChat = 0x020,
        SquadChat = 0x040,
        RoundOver = 0x080,
        RoundStart = 0x100,
        TeamChange = 0x200,
        Leave = 0x400
    };



    public enum Actions
    {
        None = 0x0000,
        Kick = 0x0001,
        Kill = 0x0002,
        PBBan = 0x0004,
        EABan = 0x0010,
        Say = 0x0020,
        Log = 0x0040,
        TaskbarNotify = 0x0080,
        Mail = 0x0100,
        SMS = 0x0200,
        Tweet = 0x0400,
        PBCommand = 0x0800,
        ServerCommand = 0x1000,
        PRoConEvent = 0x2000,
        PRoConChat = 0x4000,
        SoundNotify = 0x8000,
        Yell = 0x10000
    }

    public enum TrueFalse
    {
        False = 0x01,
        True = 0x02
    };

    public enum AcceptDeny
    {
        Accept = 0x01,
        Deny = 0x02
    }

    public enum LimitChoice
    {
        All = 0x01,
        NotCompiled = 0x02
    };

    public enum ShowHide
    {
        Show = 0x01,
        Hide = 0x02
    }

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

    public interface TeamInfoInterface
    {
        List<PlayerInfoInterface> players { get; }

        Double KillsRound { get; }
        Double DeathsRound { get; }
        Double SuicidesRound { get; }
        Double TeamKillsRound { get; }
        Double TeamDeathsRound { get; }
        Double HeadshotsRound { get; }
        Double ScoreRound { get; }

        Int32 TeamId { get; }
        Double TicketsRound { get; } /* deprecated */
        Double Tickets { get; }
        Double RemainTickets { get; }
        Double RemainTicketsPercent { get; }
        Double StartTickets { get; }

        // BF4
        Int32 Faction { get; }
    }

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
        DataDictionaryInterface DataRound { get; }   //this dictionary is automatically cleared OnRoundStart

        /* Other methods */
        String LogFile { get; }

    }

    public interface BattlelogWeaponStatsInterface
    {
        Double Accuracy { get; }
        Double Kills { get; }
        Double Headshots { get; }
        Double ShotsFired { get; }
        Double ShotsHit { get; }
        String Name { get; }
        String Slug { get; }
        String Category { get; }
        String Code { get; }
        Double TimeEquipped { get; }
    }

    public interface WeaponStatsInterface
    {

        Double KillsRound { get; }
        Double DeathsRound { get; }
        Double SuicidesRound { get; }
        Double TeamKillsRound { get; }
        Double TeamDeathsRound { get; }
        Double HeadshotsRound { get; }

        Double KillsTotal { get; }
        Double DeathsTotal { get; }
        Double SuicidesTotal { get; }
        Double TeamKillsTotal { get; }
        Double TeamDeathsTotal { get; }
        Double HeadshotsTotal { get; }
    }

    public interface ClanStatsInterface
    {

        Double KillsRound { get; }
        Double DeathsRound { get; }
        Double SuicidesRound { get; }
        Double TeamKillsRound { get; }
        Double TeamDeathsRound { get; }
        Double HeadshotsRound { get; }

        Double KillsTotal { get; }
        Double DeathsTotal { get; }
        Double SuicidesTotal { get; }
        Double TeamKillsTotal { get; }
        Double TeamDeathsTotal { get; }
        Double HeadshotsTotal { get; }
    }


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
        List<String> MapFileNameRotation { get; }
        List<String> GamemodeRotation { get; }
        List<Int32> LevelRoundsRotation { get; }

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
        Double RoundsTotal { get; }              // Round played since plugin enabled

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
        Int32 GetFaction(Int32 TeamId);


        /* Team data */
        Double Tickets(Int32 TeamId);              // tickets for the specified team
        Double RemainTickets(Int32 TeamId);        // tickets remaining on specified team
        Double RemainTicketsPercent(Int32 TeamId); // tickets remaining on specified team (as percent)

        Double StartTickets(Int32 TeamId);         // tickets at the begining of round for specified team
        Double TargetTickets { get; }            // tickets needed to win


        Int32 OppositeTeamId(Int32 TeamId);
        Int32 WinTeamId { get; } //id of the team that won previous round

        /* Data Repository set/get custom data */

        DataDictionaryInterface Data { get; }        //this dictionary is user-managed
        DataDictionaryInterface RoundData { get; }   //this dictionary is automatically cleared OnRoundStart
        DataDictionaryInterface DataRound { get; }   //this dictionary is automatically cleared OnRoundStart
    }

    public interface KillInfoInterface
    {
        DateTime Time { get; }
        String Weapon { get; }
        Boolean Headshot { get; }
        String Category { get; } // ToString of DamageTypes
    }

    public interface KillReasonInterface
    {
        String Name { get; } // weapon name or reason, like "Suicide"
        String Detail { get; } // BF4: ammo or attachment
        String AttachedTo { get; } // BF4: main weapon when Name is a secondary attachment, like M320
        String VehicleName { get; } // BF4: if Name is "Death", this is the vehicle's name
        String VehicleDetail { get; } // BF4: if Name is "Death", this is the vehicles detail (stuff after final slash)
    }

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
        Double ScoreCombat { get; }
        Double ScoreVehicle { get; }
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
        String CountryCode { get; }
        String CountryName { get; }
        String PBGuid { get; }
        String EAGuid { get; }
        Int32 TeamId { get; }
        Int32 SquadId { get; }
        Int32 Ping { get; }
        Int32 MaxPing { get; }
        Int32 MinPing { get; }
        Int32 MedianPing { get; }
        Int32 AveragePing { get; }
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
        Boolean Battlelog404 { get; } // True - Player has no PC Battlelog profile
        Boolean StatsError { get; }   // True - Error occurred while processing player stats


        /* Whitelist information */
        Boolean inClanWhitelist { get; }
        Boolean inPlayerWhitelist { get; }
        Boolean isInWhitelist { get; }


        /* Data Repository set/get custom data */

        DataDictionaryInterface Data { get; }        //this dictionary is user-managed
        DataDictionaryInterface RoundData { get; }   //this dictionary is automatically cleared OnRoundStart
        DataDictionaryInterface DataRound { get; }   //this dictionary is automatically cleared OnRoundStart

        /* Killer/Victim Data */

        Dictionary<String, List<KillInfoInterface>> TeamKillVictims { get; }
        Dictionary<String, List<KillInfoInterface>> TeamKillKillers { get; }
        Dictionary<String, List<KillInfoInterface>> Victims { get; }
        Dictionary<String, List<KillInfoInterface>> Killers { get; }

    }


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

        List<String> GetReservedSlotsList();

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

        String FriendlySpan(TimeSpan span);         //converts a TimeSpan into a friendly formatted String e.g. "2 hours, 20 minutes, 15 seconds"
        String BestPlayerMatch(String name);        //looks in the internal player's list, and finds the best match for the given player name

        Boolean IsInGameCommand(String text);          //checks if the given text start with one of these characters: !/@?
        Boolean IsCommand(String text);                //checks if the given text start with one of these characters: !/@?

        String ExtractInGameCommand(String text);   //if given text starts with one of these charactets !/@? it removes them
        String ExtractCommand(String text);         //if given text starts with one of these charactets !/@? it removes them

        String ExtractCommandPrefix(String text);   //if given text starts with one of these chracters !/@? it returns the character

        Boolean CheckAccount(String name, out Boolean canKill, out Boolean canKick, out Boolean canBan, out Boolean canMove, out Boolean canChangeLevel);

        Double CheckPlayerIdle(String name); // -1 if unknown, otherwise idle time in seconds

        /* Updated every update_interval seconds */
        Boolean IsSquadLocked(Int32 TeamId, Int32 SquadId); // False if unknown or open, True if locked
        String GetSquadLeaderName(Int32 TeamId, Int32 SquadId); // null if unknown, player name otherwise

        /* This method looks in the internal player's list for player with matching name.
         * If fuzzy argument is set to true, it will find the player name that best matches the given name
         */
        PlayerInfoInterface GetPlayer(String name, Boolean fuzzy);

        /*
         * Creates a file in ProCOn's directory  (InsaneLimits.dump)
         * Detailed information about the exception.
         */
        void DumpException(Exception e);
        void DumpException(Exception e, String prefix);

        /* Data Repository set/get custom data */
        DataDictionaryInterface Data { get; }        //this dictionary is user-managed
        DataDictionaryInterface RoundData { get; }   //this dictionary is automatically cleared OnRoundStart
        DataDictionaryInterface DataRound { get; }   //this dictionary is automatically cleared OnRoundStart

        /* Friendly names */
        String FriendlyMapName(String mapFileName);  //example: "MP_001" -> "Grand Bazaar"
        String FriendlyModeName(String modeName);    //example: "TeamDeathMatch0" -> "TDM"
        KillReasonInterface FriendlyWeaponName(String killWeapon);
        // BF3 example: "Weapons/XP2_L86/L86" => KillReason("L86", null, null)
        // BF4 example: "U_AK12_M320_HE" => KillReason("M320", "HE", "AK12")
        // BF4 vehicle example: "Gameplay/Vehicles/AH6/AH6_Littlebird" => KillReason("Death", null, null, "AH6", "AH6_Littlebird")

        /* External plugin support */
        Boolean IsOtherPluginEnabled(String className, String methodName);
        void CallOtherPlugin(String className, String methodName, Hashtable parms);
        DateTime GetLastPluginDataUpdate(); // return timestamp for the last time InsaneLimits.UpdatePluginData() was called
    }





    public partial class InsaneLimits : PRoConPluginAPI, IPRoConPluginInterface, PluginInterface
    {
        public String server_host = String.Empty;
        public String server_port = String.Empty;
        public String server_name = String.Empty;
        public String server_desc = String.Empty;

        public String[] Carriers = new String[]
        {
            /* Country, Carrier, Gateway */

            "Argentina", "Claro", "number@sms.ctimovil.com.ar",
            "Argentina", "Movistar", "number@sms.movistar.net.ar",
            "Argentina", "Nextel", "TwoWay.11number@nextel.net.ar",
            "Argentina", "Personal", "number@alertas.personal.com.ar",
            "Aruba", "Setar Mobile", "number@mas.aw",
            "Australia", "T-Mobile(Optus Zoo)", "0number@optusmobile.com.au",

            "Austria", "T-Mobile", "number@sms.t-mobile.at",
            "Brasil", "Claro", "number@clarotorpedo.com.br",
            "Brasil", "Vivo", "number@torpedoemail.com.br",
            "Bulgaria", "Globul", "35989number@sms.globul.bg",
            "Bulgaria", "Mobiltel", "35988number@sms.mtel.net",
            "Bulgaria", "Vivacom", "35987number@sms.vivacom.bg",
            "Canada", "Aliant", "number@sms.wirefree.informe.ca",
            "Canada", "Bell Mobility", "number@txt.bell.ca",
            "Canada", "Solo Mobile", "number@txt.bell.ca",
            "Canada", "Fido", "number@fido.ca",
            "Canada", "Koodo Mobile", "number@msg.telus.com",
            "Canada", "MTS Mobility", "number@text.mtsmobility.com",
            "Canada", "PC Telecom", "number@mobiletxt.ca",
            "Canada", "Rogers Wireless", "number@pcs.rogers.com",
            "Canada", "SaskTel", "number@sms.sasktel.com",
            "Canada", "Telus Mobility", "number@msg.telus.com",
            "Canada", "Virgin Mobile", "number@vmobile.ca",
            "China", "China Mobile", "number@139.com",
            "Colombia", "Comcel", "number@comcel.com.co",
            "Colombia", "Movistar", "number@movistar.com.co",
            "Colombia", "Tigo (Formerly Ola)", "number@sms.tigo.com.co",
            "Costa Rica", "ICE", "number@ice.cr",
            "Europe", "TellusTalk", "number@esms.nu",
            "France", "Bouygues Telecom", "number@mms.bouyguestelecom.fr",
            "Germany", "E-Plus", "0number@smsmail.eplus.de",
            "Germany", "O2", "0number@o2online.de",
            "Germany", "T-Mobile", "number@t-mobile-sms.de",
            "Germany", "Vodafone", "0number@vodafone-sms.de",
            "Hong Kong", "CSL", "number@mgw.mmsc1.hkcsl.com",
            "Hungary", "Ozeki", "number@ozekisms.com",
            "Iceland", "OgVodafone", "number@sms.is",
            "Iceland", "Siminn", "number@box.is",
            "India", "Aircel", "number@aircel.co.in",
            "India", "Andhra Pradesh Aircel", "number@airtelap.com",
            "India", "Karnataka Airtel", "number@airtelkk.com",
            "India", "Andhra Pradesh AirTel", "91number@airtelap.com",
            "India", "Andhra Pradesh Idea Cellular", "number@ideacellular.net",
            "India", "Chennai Skycell / Airtel", "919840number@airtelchennai.com",
            "India", "Chennai RPG Cellular", "9841number@rpgmail.net",
            "India", "Delhi Airtel", "919810number@airtelmail.com",
            "India", "Delhi Hutch", "9811number@delhi.hutch.co.in",
            "India", "Goa Airtel", "919890number@airtelmail.com",
            "India", "Goa Idea Cellular", "number@ideacellular.net",
            "India", "Goa BPL Mobile", "9823number@bplmobile.com",
            "India", "Gujarat Idea Cellular", "number@ideacellular.net",
            "India", "Gujarat Airtel", "919898number@airtelmail.com",
            "India", "Gujarat Celforce / Fascel", "9825number@celforce.com",
            "India", "Haryana Airtel", "919896number@airtelmail.com",
            "India", "Haryana Escotel", "9812number@escotelmobile.com",
            "India", "Himachai Pradesh Airtel", "919816number@airtelmail.com",
            "India", "Karnataka Airtel", "919845number@airtelkk.com",
            "India", "Kerala Airtel", "919895number@airtelkerala.com",
            "India", "Kerala BPL Mobile", "9846number@bplmobile.com",
            "India", "Kerala Escotel", "9847number@escotelmobile.com",
            "India", "Koltaka Airtel", "919831number@airtelkol.com",
            "India", "Madhya Pradesh Airtel", "919893number@airtelmail.com",
            "India", "Maharashtra Airtel", "919890number@airtelmail.com",
            "India", "Maharashtra BPL Mobile", "9823number@bplmobile.com",
            "India", "Maharashtra Idea Cellular", "number@ideacellular.net",
            "India", "Mumbai Airtel", "919892number@airtelmail.com",
            "India", "Mumbai BPL Mobile", "9821number@bplmobile.com",
            "India", "Pondicherry BPL Mobile", "9843number@bplmobile.com",
            "India", "Punjab Airtel", "919815number@airtelmail.com",
            "India", "Tamil Nadu Airtel", "919894number@airtelmobile.com",
            "India", "Tamil Nadu Aircel", "9842number@airsms.com",
            "India", "Tamil Nadu BPL Mobile", "919843number@bplmobile.com",
            "India", "Uttar Pradesh West Escotel", "9837number@escotelmobile.com",
            "India", "Loop (BPL Mobile), Mumbai", "number@loopmobile.co.in",
            "Ireland", "Meteor", "number@sms.mymeteor.ie",
            "Israel", "Spikko", "number@SpikkoSMS.com",
            "Italy", "TIM", "0number@timnet.com",
            "Japan", "AU, KDDI", "number@ezweb.ne.jp",
            "Japan", "NTT DoCoMo", "number@docomo.ne.jp",
            "Japan", "Vodafone, Chuugoku/Western", "number@n.vodafone.ne.jp",
            "Japan", "Vodafone, Hokkaido", "number@d.vodafone.ne.jp",
            "Japan", "Vodafone, Hokuriko/Central North", "number@r.vodafone.ne.jp",
            "Japan", "Vodafone, Kansai/West/Osaka", "number@k.vodafone.ne.jp",
            "Japan", "Vodafone, Kanto/Koushin/East", "number@t.vodafone.ne.jp",
            "Japan", "Vodafone, Kyuushu/Okinawa", "number@q.vodafone.ne.jp",
            "Japan", "Vodafone, Skikoku", "number@s.vodafone.ne.jp",
            "Japan", "Vodafone, Touhoku/Niigata/North", "number@h.vodafone.ne.jp",
            "Japan", "Willcom", "number@pdx.ne.jp",
            "Japan", "Willcom DJ", "number@dj.pdx.ne.jp",
            "Japan", "Willcom DI", "number@di.pdx.ne.jp",
            "Japan", "Willcom DK", "number@dk.pdx.ne.jp",
            "Mauritius", "Emtel", "number@emtelworld.net",
            "Mexico", "Nextel", "number@msgnextel.com.mx",
            "Nepal", "Mero Mobile", "977number@sms.spicenepal.com",
            "Netherlands", "Orange", "0number@sms.orange.nl",
            "Netherlands", "T-Mobile", "31number@gin.nl",
            "New Zealand", "Telecom New Zealand", "number@etxt.co.nz",
            "New Zealand", "Vodafone", "number@mtxt.co.nz",
            "Nicaragua", "Claro", "number@ideasclaro-ca.com",
            "Norway", "Sendega", "number@sendega.com",
            "Poland", "Orange Polska", "9digit@orange.pl",
            "Poland", "Plus", "+number@text.plusgsm.pl",
            "Puerto Rico", "Claro", "number@vtexto.com",
            "Singapore", "M1", "number@m1.com.sg",
            "South Africa", "MTN", "number@sms.co.za",
            "South Africa", "Vodacom", "number@voda.co.za",
            "South Korea", "Helio", "number@myhelio.com",
            "Spain", "Esendex", "number@esendex.net",
            "Spain", "Movistar", "0number@movistar.net",
            "Spain", "Vodafone", "0number@vodafone.es",
            "Singapore", "Starhub Enterprise Messaging Solution", "number@starhub-enterprisemessaging.com",
            "Sri Lanka", "Mobitel", "number@sms.mobitel.lk",
            "Sweden", "Tele2", "0number@sms.tele2.se",
            "Switzerland", "Sunrise Communications", "number@gsm.sunrise.ch",
            "United States", "Alaska Communications", "number@msg.acsalaska.com",
            "United States", "Alltel (Allied Wireless)", "number@sms.alltelwireless.com",
            "United States", "Verizon Wireless (Alltel Merger)", "number@text.wireless.alltel.com",
            "United States", "Ameritech", "number@paging.acswireless.com",
            "United States", "ATT Wireless", "number@txt.att.net",
            "United States", "ATT Enterprise Paging", "number@page.att.net",
            "United States", "ATT Global Smart Suite", "number@sms.smartmessagingsuite.com",
            "United States", "BellSouth", "number@bellsouth.cl",
            "United States", "Bluegrass Cellular", "number@sms.bluecell.com",
            "United States", "Bluesky Communications, Samoa", "number@psms.bluesky.as",
            "United States", "Boost Mobile", "number@myboostmobile.com",
            "United States", "Cellcom", "number@cellcom.quiktxt.com",
            "United States", "Cellular One", "number@mobile.celloneusa.com",
            "United States", "Cellular South", "number@csouth1.com",
            "United States", "Centenial Wireless", "number@cwemail.com",
            "United States", "Cariton Valley Wireless", "number@sms.cvalley.net",
            "United States", "Cincinnati Bell", "number@gocbw.com",
            "United States", "Cingular", "number@cingular.com",
            "United States", "Cingular (GoPhone)", "number@cingulartext.com",
            "United States", "Cleartalk Wireless", "number@sms.cleartalk.us",
            "United States", "Cricket", "number@sms.mycricket.com",
            "United States", "Edge Wireless", "number@sms.edgewireless.com",
            "United States", "Element Mobile", "number@SMS.elementmobile.net",
            "United States", "Esendex", "number@echoemail.net",
            "United States", "General Communications", "number@mobile.gci.net",
            "United States", "Golden State Cellular", "number@gscsms.com",
            "United States", "Hawaii Telcom Wireless", "number@hawaii.sprintpcs.com",
            "United States", "Helio", "number@myhelio.com",
            "United States", "Kajeet", "number@mobile.kajeet.net",
            "United States", "MetroPCS", "number@mymetropcs.com",
            "United States", "Nextel", "number@messaging.nextel.com",
            "United States", "O2", "number@mobile.celloneus.com",
            "United States", "Orange", "number@mobile.celloneus.com",
            "United States", "PagePlus Cellular", "number@vtext.com",
            "United States", "Pioneer Cellular", "number@zsend.com",
            "United States", "Pocket Wireless", "number@sms.pocket.com",
            "United States", "TracFone (prepaid)", "number@mmst5.tracfone.com",
            "United States", "Sprint (PCS)", "number@messaging.sprintpcs.com",
            "United States", "Nextel (Sprint)", "number@page.nextel.com",
            "United States", "Straight Talk", "number@vtext.com",
            "United States", "Syringa Wireless", "number@rinasms.com",
            "United States", "T-Mobile", "number@tmomail.net",
            "United States", "Teleflip", "number@teleflip.com",
            "United States", "Telus Mobility", "number@msg.telus.com",
            "United States", "Unicel", "number@utext.com",
            "United States", "US Cellular", "number@email.uscc.net",
            "United States", "US Mobility", "number@usmobility.net",
            "United States", "Verizon Wireless", "number@vtext.com",
            "United States", "Viaero", "number@viaerosms.com",
            "United States", "Virgin Mobile", "number@vmobl.com",
            "United States", "XIT Communications" , "number@sms.xit.net",
            "United States", "Qwest Wireless", "number@qwestmp.com",
            "United States", "Rogers Wireless", "number@pcs.rogers.com",
            "United States", "Simple Mobile", "number@smtext.com",
            "United States", "South Central Communications", "number@rinasms.com",
            "United Kingdom", "AQL", "number@text.aql.com",
            "United Kingdom", "Esendex","number@echoemail.net",
            "United Kingdom", "HSL", "number@sms.haysystems.com",
            "United Kingdom", "My-Cool-SMS", "number@my-cool-sms.com",
            "United Kingdom", "O2", "44number@mmail.co.uk",
            "United Kingdom", "Orange", "number@orange.net",
            "United Kingdom", "Txtlocal", "number@txtlocal.co.uk",
            "United Kingdom", "T-Mobile", "0n@t-mobile.uk.net",
            "United Kingdom", "UniMovil Corporation", "number@viawebsms.com",
            "United Kingdom", "Virgin Mobile", "number@vxtras.com",
            "Worldwide", "Panacea Mobile", "number@api.panaceamobile.com"

        };

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
             * fn  - full name
             */
            "%k_n%",    "Killer name",
            "%k_ct%",   "Killer clan-Tag",
            "%k_cn%",   "Killer county-name",
            "%k_cc%",   "Killer county-code",
            "%k_ip%",   "Killer ip-address",
            "%k_eg%",   "Killer EA GUID",
            "%k_pg%",   "Killer Punk-Buster GUID",
            "%k_fn%",   "Killer full name, includes Clan-Tag (if any)",

            // Victim Replacements (Evaluations:  OnKill, OnDeath, OnTeamKills, and OnTeamDeath)

            /* Legend:
             * v   - victim
             */
            "%v_n%",    "Victim name",
            "%v_ct%",   "Victim clan-Tag",
            "%v_cn%",   "Victim county-name",
            "%v_cc%",   "Victim county-code",
            "%v_ip%",   "Victim ip-address",
            "%v_eg%",   "Victim EA GUID",
            "%v_pg%",   "Vitim Punk-Buster GUID",
            "%v_fn%",   "Victim full name, includes Clan-Tag (if any)",

            // Player Repalcements (Evaluations: OnJoin, OnSpawn, OnAnyChat, OnTeamChange, and OnSuicide)

            /* Legend:
             * p   - player
             * txt - Chat Text
             */
            "%p_n%",    "Player name",
            "%p_ct%",   "Player clan-Tag",
            "%p_cn%",   "Player county-name",
            "%p_cc%",   "Player county-code",
            "%p_ip%",   "Player ip-address",
            "%p_eg%",   "Player EA GUID",
            "%p_pg%",   "Player Punk-Buster GUID",
            "%p_fn%",   "Player full name, includes Clan-Tag (if any)",
            "%p_lc%",   "Player, Text of last chat",
            // Weapon Replacements (Evaluations: OnKill, OnDeath, OnTeamKill, OnTeamDeath, OnSuicide)

            /* Legend:
             * w   - weapon
             * n   - name
             * p   - player
             * a   - All (players)
             * x   - count
             */
            "%w_n%",    "Weapon name",
            "%w_p_x%",  "Weapon, number of times used by player in current round",
            "%w_a_x%",  "Weapon, number of times used by All players in current round",

            // Limit Replacements for Activations & Spree Counts (Evaluations: Any) ... (Current Round)

            /* Legend:
             * th  - ordinal count suffix e.g. 1st, 2nd, 3rd, 4th, etc
             * x   - count, 1, 2, 3, 4, etc
             * p   - player
             * s   - squad
             * t   - team
             * a   - All (players)
             * r   - SpRee
             */
            "%p_x_th%",  "Limit, ordinal number of times limit has been activated by the player",
            "%s_x_th%",  "Limit, ordinal number of times limit has been activated by the player's squad",
            "%t_x_th%",  "Limit, ordinal number of times limit has been activated by the player's team",
            "%a_x_th%",  "Limit, ordinal number of times limit has been activated by all players in the server",
            "%r_x_th%",  "Limit, ordinal number of times limit has been activated by player without Spree value being reset",
            "%p_x%",     "Limit, number of times limit has been activated by the player",
            "%s_x%",     "Limit, number of times limit has been activated by the player's squad",
            "%t_x%",     "Limit, number of times limit has been activated by the player's team",
            "%a_x%",     "Limit, number of times limit has been activated by all players in the server",
            "%r_x%",     "Limit, number of times limit has been activated by player without Spree value being reset",

            // Limit Replacements for Activations & Spree Counts (Evaluations: Any) ... (All Rounds)
            /* Legend:
             * xa - Total count, for all rounds
             */
            "%p_xa_th%",  "Limit, ordinal number of times limit has been activated by the player",
            "%s_xa_th%",  "Limit, ordinal number of times limit has been activated by the player's squad",
            "%t_xa_th%",  "Limit, ordinal number of times limit has been activated by the player's team",
            "%a_xa_th%",  "Limit, ordinal number of times limit has been activated by all players in the server",
            "%p_xa%",     "Limit, number of times limit has been activated by the player",
            "%s_xa%",     "Limit, number of times limit has been activated by the player's squad",
            "%t_xa%",     "Limit, number of times limit has been activated by the player's team",
            "%a_xa%",     "Limit, number of times limit has been activated by all players in the server",


            "%date%", "Current date, e.g. Sunday December 25, 2011",
            "%time%", "Current time, e.g. 12:00 AM",

            "%server_host%", "Server/Layer host/IP, ",
            "%server_port%", "Server/Layer port number",

            "%l_id%", "Limit numeric id",
            "%l_n%", "Limit name",

        };



        public static String NL = System.Environment.NewLine;

        public Boolean plugin_enabled = false;
        Boolean plugin_activated = false;
        String oauth_token = String.Empty;
        String oauth_token_secret = String.Empty;
        Boolean round_over = false; // overloaded to also mean OnRoundOver limits evaled
        //bool sleeping = false;
        public ServerInfo serverInfo = null;
        List<MaplistEntry> mapList = null;
        public DateTime enabledTime;

        Int32 curMapIndex = 0;
        Int32 nextMapIndex = 0;
        public Double ctfRoundTimeModifier = 0;
        public Double gameModeCounter = 0;
        private List<String> lockedSquads = new List<String>();
        private Dictionary<String, String> squadLeaders = new Dictionary<String, String>();

        public Dictionary<String, DamageTypes> WeaponsDict = null;

        public BattleLog blog = null;
        public Dictionary<String, Single> floatVariables;
        public Dictionary<String, String> stringListVariables;
        public Dictionary<String, String> stringVariables;
        public Dictionary<String, Boolean> booleanVariables;
        public Dictionary<String, Int32> integerVariables;

        public Dictionary<String, integerVariableValidator> integerVarValidators;
        public Dictionary<String, booleanVariableValidator> booleanVarValidators;
        public Dictionary<String, stringVariableValidator> stringVarValidators;
        public Dictionary<String, floatVariableValidator> floatVarValidators;

        public delegate Boolean integerVariableValidator(String var, Int32 value);
        public delegate Boolean booleanVariableValidator(String var, Boolean value);
        public delegate Boolean stringVariableValidator(String var, String value);
        public delegate Boolean floatVariableValidator(String var, Single value);

        static Dictionary<String, String> json2key = new Dictionary<String, String>();
        static Dictionary<String, String> json2keyBF4 = new Dictionary<String, String>();
        static Dictionary<String, String> gamekeys = new Dictionary<String, String>();
        static Dictionary<String, String> wjson2prop = new Dictionary<String, String>();

        public Dictionary<String, PlayerInfo> players;
        public Dictionary<String, List<String>> settings_group;
        public Dictionary<String, Boolean> hidden_variables;
        public List<String> exported_variables;
        public Dictionary<String, Int32> settings_group_order;
        public CodeDomProvider compiler;
        public CompilerParameters compiler_parameters;
        public Dictionary<String, String> ReplacementsDict;
        public Dictionary<String, String> AdvancedReplacementsDict;
        public Dictionary<String, Dictionary<String, String>> CarriersDict;

        public DataDictionary DataDict;
        public DataDictionary RoundDataDict;
        private DateTime last_data_change;
        private MatchCommand match_command_update_plugin_data;


        EventWaitHandle fetch_handle;
        EventWaitHandle enforcer_handle;
        EventWaitHandle settings_handle;
        EventWaitHandle say_handle;
        EventWaitHandle info_handle;
        EventWaitHandle scratch_handle;
        EventWaitHandle list_handle;
        EventWaitHandle indices_handle;
        EventWaitHandle server_name_handle;
        EventWaitHandle server_desc_handle;
        EventWaitHandle activate_handle = new EventWaitHandle(false, EventResetMode.ManualReset);
        EventWaitHandle plist_handle;
        EventWaitHandle move_handle;
        EventWaitHandle pending_handle;
        EventWaitHandle reply_handle;

        Thread fetching_thread;
        Thread enforcer_thread;
        Thread say_thread;
        Thread settings_thread;
        Thread finalizer;
        Thread moving_thread;


        public Object players_mutex = new Object();
        public Object settings_mutex = new Object();
        public Object message_mutex = new Object();
        public Object moves_mutex = new Object();
        public Object evaluation_mutex = new Object();
        public Object lists_mutex = new Object();
        public Object limits_mutex = new Object();
        public Object updates_mutex = new Object();


        public Dictionary<String, Limit> limits;
        public Dictionary<String, CustomList> lists;


        public static String default_PIN_message = "Navigate to Twitter's authorization site to obtain the PIN";
        public static String default_twitter_consumer_key = "USQmPjXO3BFLDfWyLoAx0g";
        public static String default_twitter_consumer_secret = "UBpq7ULrfaXe1xLFL4xnAoBFnQ0GVsP2tdJXIRdLbVA";
        public static String default_twitter_access_token = "475558195-h1DII1daqUjvK1KJ8x5CD9taTIeuDq9JqMgQkGva";
        public static String default_twitter_access_token_secret = "LvHjwGMQTfE0f59kRBkE1tBjsz4KYhyh6pzS4iCdkxA";
        public static String default_twitter_user_id = "475558195";
        public static String default_twitter_screen_name = "InsaneLimits";

        public Dictionary<String, String> rcon2bw;
        public Dictionary<String, String> rcon2bwbf4;
        public List<String> rcon2bw_user_var; // user define mappings
        public Dictionary<String, String> rcon2bw_user;
        public Dictionary<String, String> cacheResponseTable;

        private Boolean isRoundReset = false;

        private Int32 expectedPBCount = 0;

        public Dictionary<String, String> friendlyMaps = null;
        public Dictionary<String, String> friendlyModes = null;

        private Boolean level_loaded = false;

        public List<String> reserved_slots_list;

        public const Single MIN_UPDATE_INTERVAL = 60;

        /* updateable var.* values */
        public Int32 varBulletDamage = -1;
        public Boolean varFriendlyFire = false;
        public Int32 varGunMasterWeaponsPreset = -1;
        public Double varIdleTimeout = -1;
        public Int32 varSoldierHealth = -1;
        public Boolean varVehicleSpawnAllowed = true;
        public Int32 varVehicleSpawnDelay = -1;
        /* BF4 */
        public Boolean varCommander = false;
        public Int32 varMaxSpectators = -1;
        public String varServerType = String.Empty;

        DateTime timerSquad = DateTime.Now;
        DateTime timerVars = DateTime.Now;

        // BF4
        public String game_version = "BF3";

        public InsaneLimits()
        {
            try
            {
                players = new Dictionary<String, PlayerInfo>();
                blog = new BattleLog(this);
                settings_group_order = new Dictionary<String, Int32>();
                hidden_variables = new Dictionary<String, Boolean>();
                exported_variables = new List<String>();
                settings_group = new Dictionary<String, List<String>>();

                this.limits = new Dictionary<String, Limit>();
                this.lists = new Dictionary<String, CustomList>();




                /* Integers */

                this.integerVariables = new Dictionary<String, Int32>();
                this.integerVariables.Add("delete_limit", 0);
                this.integerVariables.Add("delete_list", 0);
                this.integerVariables.Add("auto_load_interval", 120);
                this.integerVariables.Add("debug_level", 3);
                this.integerVariables.Add("smtp_port", 587);
                this.integerVariables.Add("wait_timeout", 30);


                this.integerVarValidators = new Dictionary<String, integerVariableValidator>();
                this.integerVarValidators.Add("delete_limit", integerValidator);
                this.integerVarValidators.Add("delete_list", integerValidator);
                this.integerVarValidators.Add("auto_load_interval", integerValidator);
                this.integerVarValidators.Add("debug_level", integerValidator);
                this.integerVarValidators.Add("smtp_port", integerValidator);
                this.integerVarValidators.Add("wait_timeout", integerValidator);

                /* Booleans */
                this.booleanVariables = new Dictionary<String, Boolean>();

                this.booleanVariables.Add("virtual_mode", true);
                this.booleanVariables.Add("save_limits", false);
                this.booleanVariables.Add("load_limits", false);

                this.booleanVariables.Add("use_white_list", false);
                this.booleanVariables.Add("use_direct_fetch", true);
                this.booleanVariables.Add("use_weapon_stats", false);
                this.booleanVariables.Add("use_battlelog_proxy", false);
                this.booleanVariables.Add("use_slow_weapon_stats", false);
                this.booleanVariables.Add("use_stats_log", false);
                this.booleanVariables.Add("use_custom_lists", false);
                this.booleanVariables.Add("use_custom_smtp", false);
                this.booleanVariables.Add("use_custom_storage", false);
                this.booleanVariables.Add("use_custom_twitter", false);
                this.booleanVariables.Add("twitter_setup_account", false);
                this.booleanVariables.Add("twitter_reset_defaults", false);
                this.booleanVariables.Add("use_custom_privacy_policy", false);
                this.booleanVariables.Add("privacy_policy_agreement", false);
                this.booleanVariables.Add("tweet_my_server_bans", true);
                this.booleanVariables.Add("tweet_my_server_kicks", true);
                this.booleanVariables.Add("tweet_my_plugin_state", true);
                this.booleanVariables.Add("auto_hide_sections", true);
                this.booleanVariables.Add("smtp_ssl", true);

                this.hidden_variables.Add("use_weapon_stats", true);

                this.booleanVarValidators = new Dictionary<String, booleanVariableValidator>();

                this.booleanVarValidators.Add("virtual_mode", booleanValidator);
                this.booleanVarValidators.Add("save_limits", booleanValidator);
                this.booleanVarValidators.Add("load_limits", booleanValidator);
                this.booleanVarValidators.Add("use_white_list", booleanValidator);
                this.booleanVarValidators.Add("use_direct_fetch", booleanValidator);
                this.booleanVarValidators.Add("use_weapon_stats", booleanValidator);
                this.booleanVarValidators.Add("use_battlelog_proxy", booleanValidator);
                this.booleanVarValidators.Add("use_slow_weapon_stats", booleanValidator);
                this.booleanVarValidators.Add("use_stats_log", booleanValidator);
                this.booleanVarValidators.Add("use_custom_lists", booleanValidator);
                this.booleanVarValidators.Add("smtp_ssl", booleanValidator);
                this.booleanVarValidators.Add("auto_hide_sections", booleanValidator);
                this.booleanVarValidators.Add("use_custom_twitter", booleanValidator);
                this.booleanVarValidators.Add("twitter_setup_account", booleanValidator);
                this.booleanVarValidators.Add("twitter_reset_defaults", booleanValidator);
                this.booleanVarValidators.Add("use_custom_privacy_policy", booleanValidator);
                this.booleanVarValidators.Add("privacy_policy_agreement", booleanValidator);





                /* Floats */
                this.floatVariables = new Dictionary<String, Single>();
                this.floatVariables.Add("say_interval", 0.05f);
                this.floatVariables.Add("update_interval", MIN_UPDATE_INTERVAL);

                this.floatVarValidators = new Dictionary<String, floatVariableValidator>();
                this.floatVarValidators.Add("say_interval", floatValidator);
                this.floatVarValidators.Add("update_interval", floatValidator);

                /* String lists */
                this.stringListVariables = new Dictionary<String, String>();
                this.stringListVariables.Add("clan_white_list", @"clan1, clan2, clan3");

                this.stringListVariables.Add("player_white_list", @"micovery, player2, player3");


                /* Strings */
                this.stringVariables = new Dictionary<String, String>();

                this.stringVariables.Add("new_limit", "...");
                this.stringVariables.Add("new_list", "...");
                this.stringVariables.Add("compile_limit", "...");
                this.stringVariables.Add("limits_file", this.GetType().Name + ".conf");
                this.stringVariables.Add("console", "Type a command here to test");
                this.stringVariables.Add("smtp_host", "smtp.gmail.com");
                this.stringVariables.Add("smtp_account", "procon.insane.limits@gmail.com");
                this.stringVariables.Add("smtp_mail", "procon.insane.limits@gmail.com");
                this.stringVariables.Add("smtp_password", Decode("dG90YWxseWluc2FuZQ=="));

                this.stringVariables.Add("proxy_url", "http://127.0.0.1:3128");


                this.stringVariables.Add("twitter_verifier_pin", default_PIN_message);
                this.stringVariables.Add("twitter_consumer_key", default_twitter_consumer_key);
                this.stringVariables.Add("twitter_consumer_secret", default_twitter_consumer_secret);
                this.stringVariables.Add("twitter_access_token", default_twitter_access_token);
                this.stringVariables.Add("twitter_access_token_secret", default_twitter_access_token_secret);
                this.stringVariables.Add("twitter_user_id", default_twitter_user_id);
                this.stringVariables.Add("twitter_screen_name", default_twitter_screen_name);


                this.hidden_variables.Add("twitter_consumer_key", true);
                this.hidden_variables.Add("twitter_consumer_secret", true);
                this.hidden_variables.Add("twitter_access_token", true);
                this.hidden_variables.Add("twitter_access_token_secret", true);
                this.hidden_variables.Add("twitter_user_id", true);
                this.hidden_variables.Add("twitter_screen_name", true);
                this.hidden_variables.Add("twitter_verifier_pin", true);


                this.stringVarValidators = new Dictionary<String, stringVariableValidator>();
                this.stringVarValidators.Add("console", stringValidator);
                this.stringVarValidators.Add("new_limit", stringValidator);
                this.stringVarValidators.Add("compile_limit", stringValidator);
                this.stringVarValidators.Add("new_list", stringValidator);
                this.stringVarValidators.Add("twitter_verifier_pin", stringValidator);




                /* Grouping settings */
                List<String> limit_manager_group = new List<String>();
                limit_manager_group.Add("delete_limit");
                limit_manager_group.Add("new_limit");
                limit_manager_group.Add("compile_limit");
                settings_group.Add(LimitManagerG, limit_manager_group);


                List<String> lists_manager_group = new List<String>();
                lists_manager_group.Add("new_list");
                lists_manager_group.Add("delete_list");
                settings_group.Add(ListManagerG, lists_manager_group);

                List<String> storage_manager = new List<String>();
                storage_manager.Add("save_limits");
                storage_manager.Add("load_limits");
                storage_manager.Add("limits_file");
                storage_manager.Add("auto_load_interval");
                settings_group.Add(StorageG, storage_manager);

                List<String> white_list_group = new List<String>();
                white_list_group.Add("clan_white_list");
                white_list_group.Add("player_white_list");
                settings_group.Add(WhitelistG, white_list_group);

                List<String> custom_stmp_group = new List<String>();
                custom_stmp_group.Add("smtp_host");
                custom_stmp_group.Add("smtp_port");
                custom_stmp_group.Add("smtp_account");
                custom_stmp_group.Add("smtp_mail");
                custom_stmp_group.Add("smtp_password");
                custom_stmp_group.Add("smtp_ssl");
                settings_group.Add(MailG, custom_stmp_group);

                List<String> custom_twitter_group = new List<String>();
                custom_twitter_group.Add("twitter_setup_account");
                custom_twitter_group.Add("twitter_reset_defaults");
                custom_twitter_group.Add("twitter_verifier_pin");
                custom_twitter_group.Add("twitter_access_token");
                custom_twitter_group.Add("twitter_access_token_secret");
                custom_twitter_group.Add("twitter_consumer_key");
                custom_twitter_group.Add("twitter_consumer_secret");
                settings_group.Add(TwitterG, custom_twitter_group);

                List<String> proxy_group = new List<String>();
                proxy_group.Add("proxy_url");
                settings_group.Add(ProxyG, proxy_group);

                List<String> privacy_policy = new List<String>();
                privacy_policy.Add("tweet_my_server_bans");
                privacy_policy.Add("tweet_my_server_kicks");
                privacy_policy.Add("tweet_my_plugin_state");
                privacy_policy.Add("privacy_policy_agreement");

                settings_group.Add(PrivacyPolicyG, privacy_policy);


                settings_group_order.Add(SettingsG, 1);
                settings_group_order.Add(PrivacyPolicyG, 2);
                settings_group_order.Add(WhitelistG, 3);
                settings_group_order.Add(MailG, 4);
                settings_group_order.Add(TwitterG, 5);
                settings_group_order.Add(StorageG, 6);
                settings_group_order.Add(ProxyG, 7);
                settings_group_order.Add(ListManagerG, 8);
                settings_group_order.Add(LimitManagerG, 9);

                /* Exported Variables are those that should live in the *conf file */
                exported_variables.Add("tweet_my_server_bans");
                exported_variables.Add("tweet_my_server_kicks");
                exported_variables.Add("tweet_my_plugin_state");
                exported_variables.Add("privacy_policy_agreement");


                /* Online keys BF3 */

                json2key.Add("rank", "rank");
                json2key.Add("kdRatio", "kdr");
                json2key.Add("timePlayed", "time");
                json2key.Add("kills", "kills");
                json2key.Add("numWins", "wins");
                json2key.Add("elo", "skill");
                json2key.Add("scorePerMinute", "spm");
                json2key.Add("totalScore", "score");
                json2key.Add("deaths", "deaths");
                json2key.Add("numLosses", "losses");


                json2key.Add("repairs", "repairs");
                json2key.Add("revives", "revives");
                json2key.Add("accuracy", "accuracy");
                json2key.Add("resupplies", "ressuplies");
                json2key.Add("quitPercentage", "quit_p");


                json2key.Add("sc_team", "sc_team");
                json2key.Add("combatScore", "sc_combat");
                json2key.Add("sc_vehicle", "sc_vehicle");
                json2key.Add("sc_objective", "sc_objective");
                json2key.Add("vehiclesDestroyed", "vehicles_killed");
                json2key.Add("killStreakBonus", "killStreakBonus");

                json2key.Add("killAssists", "killAssists");
                json2key.Add("rsDeaths", "rsDeaths");
                json2key.Add("rsKills", "rsKills");
                json2key.Add("rsNumLosses", "rsNumLosses");
                json2key.Add("rsNumWins", "rsNumWins");
                json2key.Add("rsScore", "rsScore");
                json2key.Add("rsShotsFired", "rsShotsFired");
                json2key.Add("rsShotsHit", "rsShotsHit");
                json2key.Add("rsTimePlayed", "rsTimePlayed");

                /* Online keys BF4 */

                json2keyBF4.Add("rank", "rank");
                json2keyBF4.Add("kdRatio", "kdr");
                json2keyBF4.Add("timePlayed", "time");
                json2keyBF4.Add("kills", "kills");
                json2keyBF4.Add("numWins", "wins");
                json2keyBF4.Add("skill", "skill"); // diff from BF3
                json2keyBF4.Add("scorePerMinute", "spm");
                json2keyBF4.Add("totalScore", "score");
                json2keyBF4.Add("deaths", "deaths");
                json2keyBF4.Add("numLosses", "losses");


                json2keyBF4.Add("repairs", "repairs");
                json2keyBF4.Add("revives", "revives");
                json2keyBF4.Add("accuracy", "accuracy");
                json2keyBF4.Add("resupplies", "ressuplies");
                json2keyBF4.Add("quitPercentage", "quit_p");


                json2keyBF4.Add("sc_team", "sc_team");
                json2keyBF4.Add("combatScore", "sc_combat");
                json2keyBF4.Add("sc_vehicle", "sc_vehicle");
                json2keyBF4.Add("sc_objective", "sc_objective");
                json2keyBF4.Add("vehiclesDestroyed", "vehicles_killed");
                json2keyBF4.Add("killStreakBonus", "killStreakBonus");

                json2keyBF4.Add("killAssists", "killAssists");
                json2keyBF4.Add("rsDeaths", "rsDeaths");
                json2keyBF4.Add("rsKills", "rsKills");
                json2keyBF4.Add("rsNumLosses", "rsNumLosses");
                json2keyBF4.Add("rsNumWins", "rsNumWins");
                json2keyBF4.Add("rsScore", "rsScore");
                json2keyBF4.Add("rsShotsFired", "rsShotsFired");
                json2keyBF4.Add("rsShotsHit", "rsShotsHit");
                json2keyBF4.Add("rsTimePlayed", "rsTimePlayed");

                /* Game keys */

                gamekeys.Add("score", "score");
                gamekeys.Add("kills", "kills");
                gamekeys.Add("deaths", "deaths");
                gamekeys.Add("tkills", "tkills");
                gamekeys.Add("tdeaths", "tdeaths");
                gamekeys.Add("headshots", "headshots");
                gamekeys.Add("suicides", "suicides");
                gamekeys.Add("rounds", "rounds");

                /* Weapon Stat Keys */

                wjson2prop.Add("category", "Category");
                wjson2prop.Add("code", "Code");
                wjson2prop.Add("headshots", "Headshots");
                wjson2prop.Add("kills", "Kills");
                wjson2prop.Add("name", "Name");
                wjson2prop.Add("shotsFired", "ShotsFired");
                wjson2prop.Add("shotsHit", "ShotsHit");
                wjson2prop.Add("slug", "Slug");
                wjson2prop.Add("timeEquipped", "TimeEquipped");


                DataDict = new DataDictionary(this);
                RoundDataDict = new DataDictionary(this);
                last_data_change = DateTime.Now;
                match_command_update_plugin_data = new MatchCommand("InsaneLimits", "UpdatePluginData", new List<String>(), "InsaneLimits_UpdatePluginData", new List<MatchArgumentFormat>(), new ExecutionRequirements(ExecutionScope.None), "External plugin support, do not use this command in-game");

                rcon2bw = new Dictionary<String, String>();

                rcon2bw["870MCS"] = "870";
                rcon2bw["AEK-971"] = "AEK971";
                rcon2bw["AKS-74u"] = "AKS74U";
                rcon2bw["AN-94 Abakan"] = "AN94";
                rcon2bw["AS Val"] = "AS-VAL";
                rcon2bw["DamageArea"] = null;
                rcon2bw["DAO-12"] = "DAO";
                rcon2bw["Death"] = null;
                rcon2bw["Defib"] = null;
                rcon2bw["FIM92"] = "fim-92-stinger";
                rcon2bw["Glock18"] = "g18";
                rcon2bw["HK53"] = "g53";
                rcon2bw["jackhammer"] = "mk3a1";
                rcon2bw["JNG90"] = "jng-90";
                rcon2bw["Knife_RazorBlade"] = "Knife";
                rcon2bw["M15 AT Mine"] = null;
                rcon2bw["M26Mass"] = "m26-mass";
                rcon2bw["M27IAR"] = "M27";
                rcon2bw["M67"] = null;
                rcon2bw["M93R"] = "93r";
                rcon2bw["Medkit"] = null;
                rcon2bw["Melee"] = null;
                rcon2bw["Model98B"] = "M98B";
                rcon2bw["PP-2000"] = "PP2000";
                rcon2bw["Repair Tool"] = null;
                rcon2bw["RoadKill"] = null;
                rcon2bw["RPK-74M"] = "RPK";
                rcon2bw["SG 553 LB"] = "SG553";
                rcon2bw["Siaga20k"] = "Saiga";
                rcon2bw["SoldierCollision"] = null;
                rcon2bw["Suicide"] = null;
                rcon2bw["Steyr AUG"] = "aug-a3";
                rcon2bw["Taurus .44"] = "Taurus 44";
                rcon2bw["USAS-12"] = "USAS";
                rcon2bw["G3A3"] = "G3A4";
                rcon2bw["C4"] = null;
                rcon2bw["Claymore"] = null;
                rcon2bw["MagpulPDR"] = "PDR";
                rcon2bw["MP412REX"] = "M412 Rex";
                rcon2bw["MP443"] = "MP 443";
                rcon2bw["MP443_GM"] = "mp443-supp";
                rcon2bw["P90_GM"] = "P90";
                rcon2bw["Sa18IGLA"] = "sa-18-igla";
                rcon2bw["SCAR-H"] = "SCAR";
                rcon2bw["UMP45"] = "UMP";
                rcon2bw["ACR"] = "acw-r";
                rcon2bw["L86"] = "l86a2";
                rcon2bw["MP5K"] = "m5k";
                rcon2bw["MTAR"] = "mtar-21";
                rcon2bw["CrossBow"] = "xbow-scoped";

                rcon2bwbf4 = new Dictionary<String, String>();

                // Strip U_ prefix and _detail suffix and upper case the key from RCON weapon code
                rcon2bwbf4["M320"] = "WARSAW_ID_P_INAME_M32MGL";
                rcon2bwbf4["CBJ-MS"] = "WARSAW_ID_P_WNAME_CBJMS";
                rcon2bwbf4["CS-LR4"] = "WARSAW_ID_P_WNAME_CSLR4";
                rcon2bwbf4["FY-JS"] = "WARSAW_ID_P_WNAME_FYJS";
                rcon2bwbf4["GALILACE"] = "WARSAW_ID_P_WNAME_GALIL21";
                rcon2bwbf4["GALILACE23"] = "WARSAW_ID_P_WNAME_GALIL23";
                rcon2bwbf4["GALILACE52"] = "WARSAW_ID_P_WNAME_GALIL52";
                rcon2bwbf4["GALILACE53"] = "WARSAW_ID_P_WNAME_GALIL53";
                rcon2bwbf4["M26MASS"] = "WARSAW_ID_P_INAME_M26_MASS";
                rcon2bwbf4["M39EBR"] = "WARSAW_ID_P_WNAME_M39";
                rcon2bwbf4["M93R"] = "WARSAW_ID_P_WNAME_93R";
                rcon2bwbf4["REPAIRTOOL"] = "WARSAW_ID_P_INAME_REPAIR";
                rcon2bwbf4["RFB"] = "WARSAW_ID_P_WNAME_RFBTARGET";
                rcon2bwbf4["SA18IGLA"] = "WARSAW_ID_P_INAME_IGLA";
                rcon2bwbf4["SAIGA"] = "WARSAW_ID_P_WNAME_SAIGA12";
                rcon2bwbf4["SCAR-H"] = "WARSAW_ID_P_WNAME_SCARH";
                rcon2bwbf4["SCAR-HSV"] = "WARSAW_ID_P_WNAME_SCARHSV";
                rcon2bwbf4["SCORPION"] = "WARSAW_ID_P_WNAME_SCORP";
                rcon2bwbf4["SCOUT"] = "WARSAW_ID_P_WNAME_SCOUTELIT";
                rcon2bwbf4["SERBUSHORTY"] = "WARSAW_ID_P_WNAME_SHORTY";
                rcon2bwbf4["SG553LB"] = "WARSAW_ID_P_WNAME_SG553";
                rcon2bwbf4["TYPE95B"] = "WARSAW_ID_P_WNAME_TYPE95B1";
                rcon2bwbf4["ULTIMAX"] = "WARSAW_ID_P_WNAME_ULTIM";
                rcon2bwbf4["USAS-12"] = "WARSAW_ID_P_WNAME_USAS12";
                rcon2bwbf4["MAGPULPDR"] = "WARSAW_ID_P_WNAME_PDR";
                rcon2bwbf4["GRENADE"] = "WARSAW_ID_P_INAME_IMPACT";
                rcon2bwbf4["CLAYMORE"] = "WARSAW_ID_P_INAME_CLAYMORE";
                rcon2bwbf4["C4"] = "WARSAW_ID_P_INAME_C4";
                rcon2bwbf4["FGM148"] = "WARSAW_ID_P_INAME_FGM148";
                rcon2bwbf4["FIM92"] = "WARSAW_ID_P_INAME_FIM92";
                rcon2bwbf4["M136"] = "WARSAW_ID_P_INAME_M136";
                rcon2bwbf4["M15"] = "WARSAW_ID_P_INAME_M15";
                rcon2bwbf4["M18"] = "WARSAW_ID_P_INAME_M18";
                rcon2bwbf4["M2"] = "WARSAW_ID_P_INAME_M2";
                rcon2bwbf4["M34"] = "WARSAW_ID_P_INAME_M34";
                rcon2bwbf4["RPG7"] = "WARSAW_ID_P_INAME_RPG7";
                rcon2bwbf4["SMAW"] = "WARSAW_ID_P_INAME_SMAW";
                rcon2bwbf4["SRAW"] = "WARSAW_ID_P_INAME_SRAW";
                rcon2bwbf4["STARSTREAK"] = "WARSAW_ID_P_INAME_STARSTREAK";
                rcon2bwbf4["XM25"] = "WARSAW_ID_P_INAME_XM25";
                rcon2bwbf4["MP412REX"] = "WARSAW_ID_P_WNAME_M412REX";
                rcon2bwbf4["SLAM"] = "WARSAW_ID_P_INAME_M2";
                rcon2bwbf4["TOMAHAWK"] = "WARSAW_ID_P_INAME_MACHETE";
                rcon2bwbf4["NLAW"] = "WARSAW_ID_P_INAME_MBTLAW";


                rcon2bwbf4["DAMAGEAREA"] = null;
                rcon2bwbf4["DEATH"] = null;
                rcon2bwbf4["DEFIB"] = null;
                //?? rcon2bwbf4["M15 AT Mine"] = null;
                rcon2bwbf4["MEDKIT"] = null;
                rcon2bwbf4["PORTABLEMEDICPACK"] = null;
                rcon2bwbf4["MELEE"] = null;
                rcon2bwbf4["ROADKILL"] = null;
                rcon2bwbf4["SOLDIERCOLLISION"] = null;
                rcon2bwbf4["SUICIDE"] = null;

                rcon2bw_user_var = new List<String>();
                rcon2bw_user = new Dictionary<String, String>();

                cacheResponseTable = new Dictionary<String, String>();

                friendlyMaps = new Dictionary<String, String>();
                friendlyModes = new Dictionary<String, String>();

                reserved_slots_list = new List<String>();
            }
            catch (Exception e)
            {
                DumpException(e);
            }
        }

        public void ResetTwitterDefaults()
        {
            ConsoleWrite("Restoring default Twitter account settings for @^b" + default_twitter_screen_name + "^n");
            setStringVarValue("twitter_verifier_pin", default_PIN_message);
            setStringVarValue("twitter_consumer_key", default_twitter_consumer_key);
            setStringVarValue("twitter_consumer_secret", default_twitter_consumer_secret);
            setStringVarValue("twitter_access_token", default_twitter_access_token);
            setStringVarValue("twitter_access_token_secret", default_twitter_access_token_secret);
            setStringVarValue("twitter_user_id", default_twitter_user_id);
            setStringVarValue("twitter_screen_name", default_twitter_screen_name);
        }

        public DataDictionaryInterface Data { get { return (DataDictionaryInterface)DataDict; } }
        public DataDictionaryInterface RoundData { get { return (DataDictionaryInterface)RoundDataDict; } }
        public DataDictionaryInterface DataRound { get { return (DataDictionaryInterface)RoundDataDict; } }


        public List<Int32> getSortedListsIds()
        {
            Dictionary<Int32, CustomList> lookup_table = new Dictionary<Int32, CustomList>();
            foreach (String listId in lists.Keys)
                lookup_table.Add(Int32.Parse(listId), lists[listId]);

            // sort the keys
            List<Int32> ids = new List<Int32>(lookup_table.Keys);

            // sort in ascending order
            ids.Sort(delegate (Int32 a, Int32 b) { return a.CompareTo(b); });
            return ids;
        }


        public List<Int32> getSortedLimitIds()
        {
            Dictionary<Int32, Limit> lookup_table = new Dictionary<Int32, Limit>();
            foreach (String limitId in limits.Keys)
                lookup_table.Add(Int32.Parse(limitId), limits[limitId]);

            // sort the keys
            List<Int32> ids = new List<Int32>(lookup_table.Keys);

            // sort in ascending order
            ids.Sort(delegate (Int32 a, Int32 b) { return a.CompareTo(b); });
            return ids;
        }

        public String getMaxListId()
        {
            Int32 max = 1;
            foreach (KeyValuePair<String, CustomList> pair in lists)
                if (Int32.Parse(pair.Key) > max)
                    max = Int32.Parse(pair.Key);

            return max.ToString();
        }

        public String getMaxLimitId()
        {
            Int32 max = 1;
            foreach (KeyValuePair<String, Limit> pair in limits)
                if (Int32.Parse(pair.Key) > max)
                    max = Int32.Parse(pair.Key);

            return max.ToString();
        }

        public String getNextListId()
        {
            if (lists.Count == 0)
                return (1).ToString();

            List<Int32> ids = getSortedListsIds();

            // no need to loop, if all slots are filled
            if (ids.Count == ids[ids.Count - 1])
                return (ids.Count + 1).ToString();

            // find the first free slot in the list
            Int32 i = 1;
            for (; i <= ids.Count; i++)
            {
                if (ids[i - 1] != i)
                    break;
            }
            return i.ToString();
        }

        public String getNextLimitId()
        {
            if (limits.Count == 0)
                return (1).ToString();

            List<Int32> ids = getSortedLimitIds();

            // no need to loop, if all slots are filled
            if (ids.Count == ids[ids.Count - 1])
                return (ids.Count + 1).ToString();

            // find the first free slot in the list
            Int32 i = 1;
            for (; i <= ids.Count; i++)
            {
                if (ids[i - 1] != i)
                    break;
            }
            return i.ToString();
        }


        public void createNewLimit()
        {

            String id = getNextLimitId();

            Limit limit = new Limit(this, id);

            lock (limits_mutex)
            {
                limits.Add(limit.id, limit);
            }

            ConsoleWrite("New " + limit.ShortName + " created");
            SaveSettings(true);
        }

        public void createNewList()
        {

            String id = getNextListId();

            CustomList list = new CustomList(this, id);

            lock (lists_mutex)
            {
                lists.Add(list.id, list);
            }

            ConsoleWrite("New " + list.ShortName + " created");
            SaveSettings(true);
        }


        private CompilerParameters GenerateCompilerParameters()
        {

            CompilerParameters parameters = new CompilerParameters();
            parameters.ReferencedAssemblies.Add("System.dll");
            parameters.ReferencedAssemblies.Add("System.Data.dll");
            parameters.ReferencedAssemblies.Add("System.Windows.Forms.dll");
            parameters.ReferencedAssemblies.Add("System.Xml.dll");
            //parameters.ReferencedAssemblies.Add("System.Linq.dll");
            if (game_version == "BF3")
                parameters.ReferencedAssemblies.Add("Plugins/BF3/InsaneLimits.dll");
            else if (game_version == "BFHL")
                parameters.ReferencedAssemblies.Add("Plugins/BFHL/InsaneLimits.dll");
            else
                parameters.ReferencedAssemblies.Add("Plugins/BF4/InsaneLimits.dll");

            parameters.GenerateInMemory = true;
            parameters.IncludeDebugInformation = false;

            String procon_path = Directory.GetParent(Application.ExecutablePath).FullName;
            String plugins_path = Path.Combine(procon_path, Path.Combine("Plugins", "BF3"));

            parameters.TempFiles = new TempFileCollection(plugins_path);
            //parameters.TempFiles.KeepFiles = false;


            return parameters;
        }
    }
}
