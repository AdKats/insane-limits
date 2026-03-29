using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

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
        public void OnPluginLoaded(String strHostName, String strPort, String strPRoConVersion)
        {

            activate_handle.Reset();

            server_host = strHostName;
            server_port = strPort;

            /* reset limits file, now that we have host and port */
            setStringVarValue("limits_file", getStringVarValue("limits_file"));

            ConsoleWrite("plugin loaded");
            this.RegisterEvents(
                "OnPlayerLeft",
                "OnPlayerJoin",
                "OnListPlayers",
                "OnPunkbusterPlayerInfo",
                "OnServerInfo",
                "OnMaplistList",
                "OnMaplistGetMapIndices",
                "OnRoundOver",
                "OnPlayerKilled",
                "OnPlayerTeamChange",
                "OnPlayerMovedByAdmin",
                "OnPlayerSpawned",
                "OnGlobalChat",
                "OnTeamChat",
                "OnSquadChat",
                "OnRoundOverPlayers",
                "OnRoundOverTeamScores",
                "OnServerName",
                "OnServerDescription",
                "OnMaplistMapAppended",
                "OnMaplistNextLevelIndex",
                "OnMaplistMapRemoved",
                "OnMaplistMapInserted",
                "OnMaplistCleared",
                "OnMaplistLoad",
                "OnMaplistSave",
                "OnEndRound",
                "OnRunNextLevel",
                "OnCurrentLevel",
                "OnLoadingLevel",
                "OnLevelStarted",
                "OnLevelLoaded",
                "OnRestartLevel",
                "OnReservedSlotsList",
                "OnGameModeCounter",
                /* R38/Procon 1.4.0.7 */
                "OnPlayerIdleDuration",
                "OnPlayerPingedByAdmin",
                "OnSquadLeader",
                "OnSquadIsPrivate",
                "OnCtfRoundTimeModifier",
                /* on update_interval get: */
                "OnBulletDamage",
                "OnFriendlyFire",
                "OnGunMasterWeaponsPreset",
                "OnIdleTimeout",
                "OnSoldierHealth",
                "OnVehicleSpawnAllowed",
                "OnVehicleSpawnDelay",
                /* BF4 additions */
                "OnCommander",
                "OnMaxSpectators",
                "OnServerType",
                "OnTeamFactionOverride"
                );

            //initialize the dictionary with countries, carriers, gateways
            initializeCarriers();
        }

        public void OnPluginEnable()
        {
            try
            {
                if (finalizer != null && finalizer.IsAlive)
                {
                    ConsoleError("Cannot enable plugin while it is finalizing");
                    return;
                }

                ConsoleWrite("^b^2Enabled!^0");
                plugin_enabled = true;
                enabledTime = DateTime.Now;

                this.players.Clear();

                Boolean cacheOK = IsCacheEnabled(true); // side-effect of logging messages
                if (!cacheOK && !getBooleanVarValue("use_direct_fetch"))
                {
                    ConsoleWarn("Player stats fetching is disabled!");
                }

                friendlyMaps.Clear();
                friendlyModes.Clear();
                List<CMap> bf3_defs = this.GetMapDefines();
                foreach (CMap m in bf3_defs)
                {
                    if (!friendlyMaps.ContainsKey(m.FileName)) friendlyMaps[m.FileName] = m.PublicLevelName;
                    if (!friendlyModes.ContainsKey(m.PlayList)) friendlyModes[m.PlayList] = m.GameMode;
                }
                if (getIntegerVarValue("debug_level") >= 8)
                {
                    foreach (KeyValuePair<String, String> pair in friendlyMaps)
                    {
                        DebugWrite("friendlyMaps[" + pair.Key + "] = " + pair.Value, 8);
                    }
                    foreach (KeyValuePair<String, String> pair in friendlyModes)
                    {
                        DebugWrite("friendlyModes[" + pair.Key + "] = " + pair.Value, 8);
                    }
                }
                DebugWrite("Friendly names loaded", 6);

                // register a command to indicate availibility to other plugins
                this.RegisterCommand(match_command_update_plugin_data);

                //start a thread that waits for the settings to be read from file

                Thread Activator = new Thread(new ThreadStart(delegate ()
                {
                    plugin_activated = false;
                    try
                    {
                        //Thread.CurrentThread.Name = "activator";
                        LoadSettings(true, false, true);
                        ConsoleWrite("Waiting for ^bprivacy_policy_agreement^n value");
                        Int32 timeout = 30;
                        while (true)
                        {
                            activate_handle.WaitOne(timeout * 1000);

                            if (!plugin_enabled)
                                break;

                            // if user has not agreed, wait for user to agree
                            if (!Agreement)
                            {
                                ConsoleWarn("You must review and accept the ^bPrivacy Policy^n before plugin can be activated");
                                activate_handle.Reset();
                                continue;
                            }

                            if (!plugin_enabled)
                                break;

                            // if user has agreed, exit now, and activate the plugin
                            if (Agreement)
                            {
                                activate_handle.Set();
                                break;
                            }
                        }

                        if (!plugin_enabled)
                        {
                            ConsoleWrite("detected that plugin was disabled, aborting");
                            return;
                        }

                        ConsoleWrite("Agreement received, activating plugin now!");
                        ActivatePlugin();

                    }
                    catch (Exception e)
                    {
                        DumpException(e);
                    }

                }));

                Activator.IsBackground = true;
                Activator.Name = "activator";
                Activator.Start();
                Thread.Sleep(1000);

            }
            catch (Exception e)
            {
                DumpException(e);
            }
        }

        public void CompileAll()
        {
            CompileAll(false);
        }
        public void CompileAll(Boolean force)
        {
            List<String> keys = new List<String>(limits.Keys);
            foreach (String key in keys)
                if (limits.ContainsKey(key) && (limits[key].evaluator == null || force) && plugin_enabled)
                    CompileLimit(limits[key]);
        }

        public void InitWeapons()
        {
            // initialize values for all known weapons

            WeaponDictionary dic = GetWeaponDefines();
            WeaponsDict = new Dictionary<String, DamageTypes>();
            foreach (Weapon weapon in dic)
                if (weapon != null && !WeaponsDict.ContainsKey(weapon.Name))
                    WeaponsDict.Add(weapon.Name, weapon.Damage);

            DebugWrite("^b" + WeaponsDict.Count + "^n weapons in dictionary", 5);

        }

        public void initializeCarriers()
        {
            if (Carriers == null)
                return;

            if (CarriersDict == null)
                CarriersDict = new Dictionary<String, Dictionary<String, String>>();

            if ((Carriers.Length % 3) != 0)
            {
                ConsoleError("sanity check failed for the ^bCarriers^n dictionary");
                return;
            }

            for (Int32 i = 0; i < Carriers.Length; i = i + 3)
            {
                String country = Carriers[i].Replace(" ", "_");
                String carrier = Carriers[i + 1].Replace(" ", "_");
                String gateway = Carriers[i + 2];

                if (!CarriersDict.ContainsKey(country))
                    CarriersDict.Add(country, new Dictionary<String, String>());

                if (!CarriersDict[country].ContainsKey(carrier))
                    CarriersDict[country].Add(carrier, String.Empty);

                CarriersDict[country][carrier] = gateway;
            }

        }

        public void InitReplacements()
        {
            if (AdvancedReplacementsDict == null)
                AdvancedReplacementsDict = new Dictionary<String, String>();

            if (ReplacementsDict == null)
                ReplacementsDict = new Dictionary<String, String>();

            if ((Replacements.Length % 2) != 0)
            {
                ConsoleError("sanity check failed for the ^bReplacements^n dictionary");
                return;
            }

            for (Int32 i = 0; i < Replacements.Length; i = i + 2)
                if (!ReplacementsDict.ContainsKey(Replacements[i]))
                    ReplacementsDict.Add(Replacements[i], Replacements[i]);
        }

        public override void OnPunkbusterPlayerInfo(CPunkbusterInfo cpbiPlayer)
        {
            DebugWrite("Got ^bOnPunkbusterPlayerInfo^n!", 10); // FIXME 8
            if (!plugin_activated)
                return;

            try
            {
                if (cpbiPlayer == null)
                    return;

                processNewPlayer(cpbiPlayer);
            }
            catch (Exception e)
            {
                DumpException(e);
            }

        }

        Dictionary<String, CPunkbusterInfo> new_player_queue = new Dictionary<String, CPunkbusterInfo>();
        public void processNewPlayer(CPunkbusterInfo cpbiPlayer)
        {
            Boolean notifyFetch = false;

            Int32 dblevel = 10;

            /* 
            For debugging, only do verbose logging fetch sent the pb player list command
            */
            if (expectedPBCount > 0)
            {
                dblevel = 8;
                --expectedPBCount;
            }

            DebugWrite("OnPunkbusterPlayerInfo::processNewPlayer locking " + cpbiPlayer.SoldierName, dblevel);

            lock (players_mutex)
            {
                if (this.players.ContainsKey(cpbiPlayer.SoldierName))
                    this.players[cpbiPlayer.SoldierName].pbInfo = cpbiPlayer;
                else
                {

                    // add new player to the queue, and wake the stats fetching loop
                    Int32 other = 10;
                    if (!new_player_queue.ContainsKey(cpbiPlayer.SoldierName))
                    {
                        other = 5;
                    }
                    DebugWrite("OnPunkbusterPlayerInfo::processNewPlayer player ^b" + cpbiPlayer.SoldierName + "^n", other);
                    if (!(new_player_queue.ContainsKey(cpbiPlayer.SoldierName) ||
                          players.ContainsKey(cpbiPlayer.SoldierName) ||
                          new_players_batch.ContainsKey(cpbiPlayer.SoldierName)))
                    {
                        DebugWrite("Queueing ^b" + cpbiPlayer.SoldierName + "^n for stats fetching", 5);
                        new_player_queue.Add(cpbiPlayer.SoldierName, cpbiPlayer);
                        notifyFetch = true;
                    }

                }
            }
            DebugWrite("OnPunkbusterPlayerInfo::processNewPlayer UNLOCKING " + cpbiPlayer.SoldierName, dblevel);
            if (notifyFetch)
            {
                //DebugWrite("signalling ^bfetch^n thread", 7); // FIXME
                //fetch_handle.Set(); // FIXME: let enforcer wake up fetch
            }
        }

        public void ResetPlayerSprees(BaseEvent type, PlayerInfo player, PlayerInfo killer, PlayerInfo victim, Kill info)
        {

            List<Limit> all = new List<Limit>();

            all.AddRange(getLimitsForEvaluation((Limit.EvaluationType)0xFFF));
            foreach (Limit limit in all)
                switch (limit.Evaluation)
                {
                    case Limit.EvaluationType.OnDeath:
                    case Limit.EvaluationType.OnTeamDeath:
                        if ((type & (BaseEvent.Kill | BaseEvent.TeamKill)) > 0)
                            limit.ResetSpree(killer.Name);
                        break;
                    case Limit.EvaluationType.OnKill:
                    case Limit.EvaluationType.OnTeamKill:
                        if ((type & (BaseEvent.Kill | BaseEvent.TeamKill)) > 0)
                            limit.ResetSpree(victim.Name);
                        break;
                    case Limit.EvaluationType.OnSuicide:
                        if ((type & (BaseEvent.Kill | BaseEvent.TeamKill)) > 0)
                            limit.ResetSpree(killer.Name);
                        break;
                    case Limit.EvaluationType.OnSpawn:
                    case Limit.EvaluationType.OnIntervalPlayers:
                    case Limit.EvaluationType.OnIntervalServer:
                    case Limit.EvaluationType.OnJoin:
                    case Limit.EvaluationType.OnLeave:
                    case Limit.EvaluationType.OnAnyChat:
                    case Limit.EvaluationType.OnRoundOver:
                    case Limit.EvaluationType.OnRoundStart:
                    case Limit.EvaluationType.OnTeamChange:
                        /*
                         case Limit.EvaluationType.OnGlobalChat:
                         case Limit.EvaluationType.OnTeamChat:
                         case Limit.EvaluationType.OnSquadChat:
                         */
                        break;
                    default:
                        ConsoleError("unknown event evaluation ^b" + limit.Evaluation.ToString() + "^n, for " + limit.ShortDisplayName);
                        break;
                }
        }

        public void evaluateLimitsForEvent(BaseEvent type, PlayerInfo player, PlayerInfo killer, PlayerInfo victim, Kill info)
        {
            DebugWrite("+++ Evaluating all ^b" + type + "^n limits ...", 6);

            KillInfo kill = new KillInfo(info, type, GetCategory(info));

            // first reset the sprees if needed
            ResetPlayerSprees(type, player, killer, victim, info);

            List<Limit> all = new List<Limit>();
            switch (type)
            {
                case BaseEvent.Kill:
                    all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnKill));
                    all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnDeath));
                    all.ForEach(delegate (Limit limit) { if (evaluateLimit(limit, killer, kill, victim)) getServerInfo(); });
                    break;
                case BaseEvent.TeamKill:
                    all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnTeamKill));
                    all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnTeamDeath));
                    all.ForEach(delegate (Limit limit) { if (evaluateLimit(limit, killer, kill, victim)) getServerInfo(); });
                    break;
                case BaseEvent.Suicide:
                    all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnSuicide));
                    all.ForEach(delegate (Limit limit) { if (evaluateLimit(limit, victim, kill)) getServerInfo(); });
                    break;
                case BaseEvent.Spawn:
                    all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnSpawn));
                    all.ForEach(delegate (Limit limit) { if (evaluateLimit(limit, player)) getServerInfo(); });
                    break;
                case BaseEvent.GlobalChat:
                    all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnAnyChat));
                    /* all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnGlobalChat)); */
                    all.ForEach(delegate (Limit limit) { if (evaluateLimit(limit, player)) getServerInfo(); });
                    break;
                case BaseEvent.TeamChat:
                    all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnAnyChat));
                    /* all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnTeamChat)); */
                    all.ForEach(delegate (Limit limit) { if (evaluateLimit(limit, player)) getServerInfo(); });
                    break;
                case BaseEvent.SquadChat:
                    all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnAnyChat));
                    /* all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnSquadChat)); */
                    all.ForEach(delegate (Limit limit) { if (evaluateLimit(limit, player)) getServerInfo(); });
                    break;

                case BaseEvent.TeamChange:
                    all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnTeamChange));
                    all.ForEach(delegate (Limit limit) { if (evaluateLimit(limit, player)) { getServerInfo(); getPlayersList(); } });
                    break;

                case BaseEvent.RoundOver:
                    all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnRoundOver));
                    all.ForEach(delegate (Limit limit) { if (evaluateLimit(limit)) getServerInfo(); });
                    break;
                case BaseEvent.RoundStart:
                    all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnRoundStart));
                    all.ForEach(delegate (Limit limit) { if (evaluateLimit(limit)) getServerInfo(); });
                    break;

                case BaseEvent.Leave:
                    all.AddRange(getLimitsForEvaluation(Limit.EvaluationType.OnLeave));
                    all.ForEach(delegate (Limit limit) { evaluateLimit(limit, player); getServerInfo(); });
                    break;
                default:

                    ConsoleError("unknown event " + type.GetType().Name + " ^b" + type.ToString());
                    return;
            }

            DebugWrite("+++ Evaluated ^b" + all.Count + "^n limits", 6);
        }

        public void UpdateStats(PlayerInfo killer, PlayerInfo victim, BaseEvent type, Kill info, String weapon)
        {
            try
            {
                if (serverInfo == null || killer == null || victim == null || info == null)
                    return;

                if (info.Headshot)
                {
                    // update the player's Headshots
                    killer.W[weapon].HeadshotsRound++;

                    // update the server's Headshots
                    serverInfo.W[weapon].HeadshotsRound++;
                }

                if (type.Equals(BaseEvent.TeamKill))
                {
                    // update the player's TeamKills/TeamDeaths
                    killer.W[weapon].TeamKillsRound++;
                    victim.W[weapon].TeamDeathsRound++;

                    //update the server's TeamKills/TeamDeaths
                    serverInfo.W[weapon].TeamKillsRound++;
                    serverInfo.W[weapon].TeamDeathsRound++;
                }
                else if (type.Equals(BaseEvent.Suicide))
                {
                    // update the player's Suicides
                    victim.W[weapon].SuicidesRound++;

                    //update the server's Suicides
                    serverInfo.W[weapon].SuicidesRound++;
                }
                else if (type.Equals(BaseEvent.Kill))
                {
                    // update player's Kills/Deaths
                    killer.W[weapon].KillsRound++;
                    victim.W[weapon].DeathsRound++;

                    // update the server's Kills/Deaths
                    serverInfo.W[weapon].KillsRound++;
                    serverInfo.W[weapon].DeathsRound++;
                }
            }
            catch (Exception) { }
        }

        public BaseEvent DetermineBaseEvent(Kill info)
        {

            // determine the event type

            if (info.IsSuicide ||
                /* Stupid Conditions For Suicides */
                info.Killer == null ||
                info.Killer.SoldierName == null ||
                info.Killer.SoldierName.Trim().Length == 0 ||
                info.Killer.GUID == null ||
                info.Killer.GUID.Trim().Length == 0
                )
                return BaseEvent.Suicide;
            else if (info.Victim.TeamID == info.Killer.TeamID)
                return BaseEvent.TeamKill;
            else
                return BaseEvent.Kill;
        }

        public static String InGameCommand_Pattern = @"^\s*([@/!\?])\s*";

        public Boolean IsCommand(String text)
        {
            return Regex.Match(text, InGameCommand_Pattern).Success;
        }

        public Boolean IsInGameCommand(String text)
        {
            return IsCommand(text);
        }

        public String ExtractCommand(String text)
        {
            return Regex.Replace(text, InGameCommand_Pattern, "").Trim();
        }

        public String ExtractInGameCommand(String text)
        {
            return ExtractCommand(text);
        }

        public String ExtractCommandPrefix(String text)
        {
            Match match = Regex.Match(text, InGameCommand_Pattern, RegexOptions.IgnoreCase);

            if (match.Success)
                return match.Groups[1].Value;

            return String.Empty;
        }

        public Boolean CheckAccount(String name, out Boolean canKill, out Boolean canKick, out Boolean canBan, out Boolean canMove, out Boolean canChangeLevel)
        {
            Boolean ret = false;
            canKill = false;
            canKick = false;
            canBan = false;
            canMove = false;
            canChangeLevel = false;
            try
            {
                if (!players.ContainsKey(name))
                {
                    DebugWrite("^1WARNING: Unable to CheckAccount for " + name + ": unrecognized name", 4);
                    return false;
                }
                CPrivileges p = this.GetAccountPrivileges(name);
                if (p == null) return false;
                ret = true;

                canKill = p.CanKillPlayers;
                canKick = p.CanKickPlayers;
                canBan = (p.CanTemporaryBanPlayers || p.CanPermanentlyBanPlayers);
                canMove = p.CanMovePlayers;
                canChangeLevel = p.CanUseMapFunctions;
            }
            catch (Exception e)
            {
                DebugWrite("EXCEPTION: CheckAccount(" + name + "): " + e.Message, 4);
                ret = false;
            }
            return ret;
        }

        public Double CheckPlayerIdle(String name) // -1 if unknown, otherwise idle time in seconds
        {
            Double ret = -1;
            try
            {
                if (String.IsNullOrEmpty(name)) return -1;

                PlayerInfo pinfo = null;

                lock (players_mutex)
                {
                    if (players.ContainsKey(name))
                    {
                        players.TryGetValue(name, out pinfo);
                    }
                }

                if (pinfo == null) return -1;
                /* old 9.12 version
                if (pinfo._idleTime == 0) {
                    ServerCommand("player.idleDuration", name); // Update it
                }
                ret = pinfo._idleTime;
                pinfo._idleTime = 0; // reset after every check
                */
                ret = pinfo._idleTime;
                pinfo._idleTime = 0; // reset on each check
                ServerCommand("player.idleDuration", name); // Update it
            }
            catch (Exception e)
            {
                DebugWrite(e.Message, 5);
            }
            return ret;
        }

        public Boolean IsSquadLocked(Int32 teamId, Int32 squadId)
        {
            String key = teamId.ToString() + "/" + squadId;
            return (lockedSquads.Contains(key));
        }

        public String GetSquadLeaderName(Int32 teamId, Int32 squadId)
        {
            String key = teamId.ToString() + "/" + squadId;
            if (squadLeaders.ContainsKey(key)) return squadLeaders[key];
            return null;
        }

        public PlayerInfoInterface GetPlayer(String name)
        {
            return GetPlayer(name, true);
        }

        public PlayerInfoInterface GetPlayer(String name, Boolean fuzzy)
        {
            if (name == null || name.Trim().Length == 0)
                return null;

            if (fuzzy)
            {
                name = BestPlayerMatch(name);

                if (name == null || name.Trim().Length == 0)
                    return null;
            }

            PlayerInfo pinfo = null;
            if (players.TryGetValue(name, out pinfo))
                return pinfo;
            else
                return null;

        }

        public void InGameCommand(String sender, String text)
        {
            try
            {

                if (!IsCommand(text))
                    return;

                String prefix = ExtractCommandPrefix(text);
                String command = ExtractCommand(text);

                // IGC begin
                DebugWrite(@"^bOriginal command^n: " + text, 6);

                Match bstatMatch = Regex.Match(command, @"^\s*bstat\s+([^\s]+)\s+([^\s]+)", RegexOptions.IgnoreCase);
                Match rstatMatch = Regex.Match(command, @"^\s*rstat\s+([^\s]+)\s+([^\s]+)", RegexOptions.IgnoreCase);
                // IGC end
                Match one1StatMatch = Regex.Match(command, @"^\s*(round|total|(?:online|battlelog|web))\s+(.+)", RegexOptions.IgnoreCase);
                Match one2StatMatch = Regex.Match(command, @"^\s*(my|[^ ]+)(?:\s+(round|total|(?:online|battlelog|web)))?\s+(.+)", RegexOptions.IgnoreCase);

                //same command, two alternatives
                Match list1StatMatch = Regex.Match(command, @"^\s*info(?:\s+(round|total|(?:online|battlelog|web)))?", RegexOptions.IgnoreCase);
                Match list2StatMatch = Regex.Match(command, @"^\s*(?:(round|total|(?:online|battlelog|web))\s+)?info", RegexOptions.IgnoreCase);

                if (list1StatMatch.Success)
                    ListStatCmd(sender, list1StatMatch.Groups[1].Value);
                else if (list2StatMatch.Success)
                    ListStatCmd(sender, list2StatMatch.Groups[1].Value);
                // IGC begin
                else if (bstatMatch.Success)
                {
                    OneStatCmd(sender, "?", bstatMatch.Groups[1].Value, "battlelog", bstatMatch.Groups[2].Value);
                }
                else if (rstatMatch.Success)
                {
                    OneStatCmd(sender, "?", rstatMatch.Groups[1].Value, "round", rstatMatch.Groups[2].Value);
                }
                // IGC end
                else if (one1StatMatch.Success)
                    OneStatCmd(sender, prefix, String.Empty, one1StatMatch.Groups[1].Value, one1StatMatch.Groups[2].Value);
                else if (one2StatMatch.Success)
                    OneStatCmd(sender, prefix, one2StatMatch.Groups[1].Value, one2StatMatch.Groups[2].Value, one2StatMatch.Groups[3].Value);

            }
            catch (Exception e)
            {
                DumpException(e);
            }

            return;

        }

        public void ListStatCmd(String sender, String scope)
        {
            if (sender == null)
                return;

            if (scope == null || scope.Length == 0 || !(scope.Equals("round") || scope.Equals("total")))
                scope = "web";

            if (!players.ContainsKey(sender))
                return;

            PlayerInfo sinfo = players[sender];

            //use reflection to go through the properties and find all the ones in the current scope
            PropertyInfo[] properties = typeof(PlayerInfo).GetProperties();

            List<String> props = new List<String>();
            foreach (PropertyInfo property in properties)
            {
                Object[] attributes = property.GetCustomAttributes(true);

                if (attributes.Length == 0 || attributes[0] == null || !attributes[0].GetType().Equals(typeof(A)))
                    continue;

                A attrs = (A)attributes[0];

                String pscope = attrs.Scope;
                String pname = attrs.Name;

                if (scope.Equals(pscope) /*&& !pname.Contains(" ")*/)
                    props.Add(pname.ToLower());
            }

            String fscope = scope.Replace("web", "battlelog");
            //format the scope name, with first letter as upper-case
            fscope = fscope.Substring(0, 1).ToUpper() + fscope.Substring(1);

            String message = fscope + " Info " + String.Join(", ", props.ToArray());

            List<String> lines = splitMessageText(message, 120);
            //send only one line to not spam the chat
            if (lines.Count > 0)
                SendGlobalMessageV(lines[0]);
        }

        public void OneStatCmd(String sender, String prefix, String player, String scope, String type)
        {
            DebugWrite(@"^bParsed command^n: " + ((sender == null) ? "(null)" : sender) + ", " + ((player == null) ? "(null)" : player) + ", " + ((scope == null) ? "(null)" : scope) + ", " + ((type == null) ? "(null)" : type), 6);

            if (sender == null)
                return;

            if (player == null || player.Length == 0 || player.Trim().Equals("my"))
                player = sender;

            // avoid command collision
            if (Regex.Match(player, @"^\s*(ban|tban|kick|kill|nuke|say|move|fmove|help|rules|grab|maps|setnext|nextlevel|restart)\s*$").Success)
                return;

            if (scope == null || scope.Length == 0 || !(scope.Equals("round") || scope.Equals("total")))
                scope = "web";

            if (!players.ContainsKey(sender))
                return;

            PlayerInfo sinfo = players[sender];

            Int32 edit_distance = 0;

            String new_player = null;
            if ((new_player = bestMatch(player, new List<String>(players.Keys), out edit_distance)) == null)
                return;

            DebugWrite(@"^bFinal command^n: " + sender + ", " + new_player + ", " + scope + ", " + ((type == null) ? "(null)" : type), 6); // IGC

            //Only allow partial matches if the commnand prefix is ?
            if (edit_distance > 0 && !prefix.Equals("?"))
                return;

            if (!players.ContainsKey(new_player))
                return;

            PlayerInfo pinfo = players[new_player];

            //use reflection to go through the properties and find the matching one
            PropertyInfo[] properties = typeof(PlayerInfo).GetProperties();
            List<String> scopes = new List<String>();

            // this is the order in which scopes are scanned, in case the stat is not found in the give scope
            scopes.Add(scope);
            scopes.Add("web");
            scopes.Add("round");
            scopes.Add("total");

            foreach (String cscope in scopes)
            {
                foreach (PropertyInfo property in properties)
                {
                    Object[] attributes = property.GetCustomAttributes(true);

                    if (attributes.Length == 0 || attributes[0] == null || !attributes[0].GetType().Equals(typeof(A)))
                        continue;

                    A attrs = (A)attributes[0];

                    String pscope = attrs.Scope;
                    String pname = attrs.Name;
                    Regex pattern = new Regex("^" + attrs.Pattern + "$", RegexOptions.IgnoreCase);

                    if (cscope.Equals(pscope) && pattern.Match(type).Success)
                    {
                        String fscope = pscope.Replace("web", "battlelog");
                        //format the scope name, with first letter as upper-case
                        fscope = fscope.Substring(0, 1).ToUpper() + fscope.Substring(1);
                        Double value = 0;

                        if (!Double.TryParse(property.GetValue((Object)pinfo, null).ToString(), out value))
                            return;

                        //make the formatted value
                        value = Math.Round(value, 2);

                        String fvalue = String.Empty;
                        if (Regex.Match(pname, "time", RegexOptions.IgnoreCase).Success)
                        {
                            TimeSpan span = TimeSpan.FromSeconds(value);
                            Int64 thours = (Int64)span.TotalHours;
                            Int64 tmins = (Int64)span.TotalMinutes;
                            Int64 hours = (Int64)span.Hours;
                            Int64 mins = (Int64)span.Minutes;
                            Int64 tsecs = (Int64)span.TotalSeconds;

                            if (thours > 0)
                            {
                                fvalue = thours + " hr" + ((thours > 1) ? "s" : "");
                                if (mins > 0)
                                    fvalue += ", " + mins + " min" + ((mins > 1) ? "s" : "");
                            }
                            else if (tmins > 0)
                                fvalue = tmins + " min" + ((tmins > 1) ? "s" : "");
                            else
                                fvalue = tsecs + " sec" + ((tsecs > 1) ? "s" : "");
                        }
                        else if (Regex.Match(pname, "percent", RegexOptions.IgnoreCase).Success)
                            fvalue = value + "%";
                        else
                            fvalue = value.ToString();

                        SendGlobalMessageV(pinfo.FullName + "'s " + fscope + " " + pname + " is " + fvalue);
                        return;
                    }
                }
            }
        }

        public override void OnGlobalChat(String sender, String text)
        {
            DebugWrite("Got ^bOnGlobalChat^n!", 8);
            if (!plugin_activated)
                return;

            PlayerInfo player = null;
            if (!players.ContainsKey(sender))
                return;
            player = players[sender];

            player.LastChat = text;

            evaluateLimitsForEvent(BaseEvent.GlobalChat, player, null, null, null);

            InGameCommand(sender, text);
        }

        public override void OnTeamChat(String sender, String text, Int32 TeamID)
        {
            DebugWrite("Got ^bOnTeamChat^n!", 8);
            if (!plugin_activated)
                return;

            PlayerInfo player = null;
            if (!players.ContainsKey(sender))
                return;
            player = players[sender];

            player.LastChat = text;

            evaluateLimitsForEvent(BaseEvent.TeamChat, player, null, null, null);

            InGameCommand(sender, text);

        }

        public override void OnSquadChat(String sender, String text, Int32 TeamID, Int32 SquadID)
        {
            DebugWrite("Got ^bOnSquadChat^n!", 8);
            if (!plugin_activated)
                return;

            PlayerInfo player = null;
            if (!players.ContainsKey(sender))
                return;
            player = players[sender];

            player.LastChat = text;

            evaluateLimitsForEvent(BaseEvent.SquadChat, player, null, null, null);

            InGameCommand(sender, text);
        }

        public override void OnPlayerSpawned(String name, Inventory inventory)
        {
            DebugWrite("Got ^bOnPlayerSpawned^n!", 8);
            if (!plugin_activated)
                return;

            /*
            Sometimes players can spawn after the OnRoundOver event,
            so to prevent spurious round start detections, wait
            until level_loaded is true.
            */
            if (round_over && !level_loaded)
            {
                DebugWrite("Skipping player spawn, level is not loaded yet!", 8);
                return;
            }

            /* 
            To avoid a situation where OnLevelLoaded executed multiple
            times in a row causes multiple OnRoundOver evals, delay
            reset of isRoundReset flag until first spawn or first
            interval limit evals (see enforcer thread).
            */
            if (isRoundReset)
            {
                DebugWrite("The round NEEDS resetting!", 8);
                isRoundReset = false;
            }

            //first player to spawn after round over, we fetch the map info again
            if (round_over == true)
            {
                DebugWrite("Marking round as in progress", 8);
                round_over = false;
                //round over, fetch map info again
                DebugWrite(":::::::::::: Round start detected ::::::::::::", 4);
                DebugWrite("async map info update", 8);
                getMapInfo();
                DebugWrite("update vars", 8);
                updateVars();
                evaluateLimitsForEvent(BaseEvent.RoundStart, null, null, null, null);
            }

            PlayerInfo player = null;
            if (!players.ContainsKey(name))
                return;
            player = players[name];

            evaluateLimitsForEvent(BaseEvent.Spawn, player, null, null, null);
        }

        public override void OnPlayerJoin(String name)
        {
            DebugWrite("Got ^bOnPlayerJoin^n! " + name, 8);
            if (!plugin_activated)
                return;
        }

        public void OnPlayerJoin(PlayerInfo player)
        {
            if (player == null)
                return;

            String name = player.Name;

            try
            {
                List<Limit> sorted_limits = getLimitsForEvaluation(Limit.EvaluationType.OnJoin);

                if (sorted_limits.Count == 0)
                {
                    DebugWrite("No valid ^bOnJoin^n or limits founds, skipping this iteration", 8);
                    return;
                }

                // refresh the map information once before all limit evals
                getMapInfoSync();

                for (Int32 i = 0; i < sorted_limits.Count; i++)
                {
                    if (!plugin_enabled)
                        break;

                    Limit limit = sorted_limits[i];

                    if (limit == null || !limit.Evaluation.Equals(Limit.EvaluationType.OnJoin))
                        continue;

                    DebugWrite("Evaluating " + limit.ShortDisplayName + " - " + limit.Evaluation.ToString() + ", for " + name, 6);
                    evaluateLimit(limit, player);
                }
            }
            catch (Exception e)
            {
                DumpException(e);
            }
        }

        public override void OnPlayerLeft(CPlayerInfo pinfo)
        {
            DebugWrite("Got ^bOnPlayerLeft^n! " + pinfo.SoldierName, 8);
            if (!plugin_activated)
                return;

            if (pinfo == null)
                return;

            String name = pinfo.SoldierName;

            PlayerInfo player = null;
            if (!players.ContainsKey(name))
                return;
            player = players[name];

            evaluateLimitsForEvent(BaseEvent.Leave, player, null, null, null);
        }

        public List<String> RecentMove = new List<String>();
        private Queue<String> moveQ = new Queue<String>();

        public override void OnPlayerMovedByAdmin(String name, Int32 TeamId, Int32 SquadId, Boolean force)
        {
            DebugWrite("Got ^bOnPlayerMovedByAdmin^n!", 8);
            if (!plugin_activated) return;

            //if player has been moved by admin, remember
            DebugWrite("ADMIN MOVE! Suppress limit eval of move for ^b" + name, 5);
            lock (moves_mutex)
            {
                if (!RecentMove.Contains(name))
                {
                    RecentMove.Add(name);
                }
            }
            pending_handle.Set(); // signal queue
            Thread.Sleep(2); // give thread some time
        }

        public void move_thread_loop()
        {
            Thread.CurrentThread.Name = "move";

            DebugWrite("starting", 4);

            DateTime since = DateTime.Now;

            /*
            As of this writing, move by admin events FOLLOW the team change event.
            Since we want to suppress move by admin team changes, we need to delay
            processing of a move event until either the next move event happens,
            until the admin event happens, or after time runs out.

            It would be better to avoid the time-out and instead trigger on whatever
            the next event is, but that means putting a signaller into every
            event handler callback.
            */
            while (plugin_enabled)
            {
                try
                {
                    Int32 n = 0;

                    lock (moves_mutex)
                    {
                        n = moveQ.Count;
                    }

                    while (n == 0)
                    {
                        DebugWrite("waiting for move event ...", 8);
                        move_handle.WaitOne(); // wait for something in the queue
                        move_handle.Reset();
                        pending_handle.Reset();
                        if (!plugin_enabled)
                        {
                            DebugWrite("detected that plugin was disabled, aborting", 4);
                            return;
                        }
                        lock (moves_mutex)
                        {
                            n = moveQ.Count;
                        }
                        DebugWrite("awake! " + n + " events in queue", 8);
                    }
                    since = DateTime.Now;

                    String name = null;

                    lock (moves_mutex)
                    {
                        name = moveQ.Dequeue();
                    }

                    if (name == null) continue;

                    lock (players_mutex)
                    {
                        if (!players.ContainsKey(name))
                        {
                            name = null;
                        }
                    }

                    if (name == null)
                    {
                        DebugWrite("looks like ^b" + name + "^n, left, skipping.", 8);
                        continue;
                    }

                    DebugWrite("Waiting to see if move for ^b" + name + "^n was an admin move ...", 8);

                    /* Add a delay to see if an admin move event comes to suppress this */

                    while (DateTime.Now.Subtract(since).TotalSeconds < 1.0)
                    {
                        pending_handle.WaitOne(200); // 1/5th second
                        lock (moves_mutex)
                        {
                            if (RecentMove.Contains(name))
                            {
                                RecentMove.Remove(name);
                                name = null;
                                break;
                            }
                        }
                    }
                    pending_handle.Reset();

                    if (name == null)
                    {
                        DebugWrite("Move was by admin, skipping ...", 8);
                        continue;
                    }
                    else
                    {
                        DebugWrite("Delay expired, assuming move is legit", 8);
                    }

                    /* Redo the test, player might have left */

                    lock (players_mutex)
                    {
                        if (!players.ContainsKey(name)) name = null;
                    }

                    if (name == null)
                    {
                        DebugWrite("looks like ^b" + name + "^n, left, skipping.", 8);
                        continue;
                    }

                    PlayerInfo pinfo = null;

                    lock (players_mutex)
                    {
                        players.TryGetValue(name, out pinfo);
                    }

                    if (pinfo == null)
                    {
                        DebugWrite("player ^b" + name + "^n isn't known yet, skipping.", 8);
                        continue;
                    }

                    DebugWrite("evaluating TeamChange limits for ^b" + name, 8);
                    evaluateLimitsForEvent(BaseEvent.TeamChange, pinfo, null, null, null);

                }
                catch (Exception e)
                {
                    if (typeof(ThreadAbortException).Equals(e.GetType()))
                    {
                        Thread.ResetAbort();
                        return;
                    }
                    DumpException(e);
                }
            }
            DebugWrite("detected that plugin was disabled, aborting", 4);
        }

        public override void OnPlayerTeamChange(String name, Int32 TeamId, Int32 SquadId)
        {
            DebugWrite("Got ^bOnPlayerTeamChange^n!", 8);
            if (!plugin_activated) return;

            try
            {
                pending_handle.Set(); // Process any previous moves that are pending
                Thread.Sleep(2); // Give thread some time

                if (TeamId <= 0)
                    return;

                PlayerInfo pinfo = null;

                lock (players_mutex)
                {
                    if (players.ContainsKey(name))
                    {
                        players.TryGetValue(name, out pinfo);
                    }
                }

                if (pinfo == null) return;

                /* nothing to do, usually happens during join */
                if (pinfo.TeamId == 0 || pinfo.TeamId == TeamId) return;

                /* if between rounds before first spawn, ignore */
                if (round_over)
                {
                    DebugWrite("Ignoring move between rounds before round start", 8);
                    return;
                }

                /* Add to queue */
                DebugWrite("Queing move of ^b" + name, 5);
                lock (moves_mutex)
                {
                    moveQ.Enqueue(name);
                }

                move_handle.Set(); // Process this move event
            }
            catch (Exception e)
            {
                DumpException(e);
            }
        }

        public override void OnPlayerKilled(Kill info)
        {
            DebugWrite("Got ^bOnPlayerKilled^n!", 8);
            if (!plugin_activated)
                return;

            try
            {
                //get the killer and victim information

                BaseEvent type = DetermineBaseEvent(info);

                CPlayerInfo killer = info.Killer;
                CPlayerInfo victim = info.Victim;

                PlayerInfo vpinfo = null;
                PlayerInfo kpinfo = null;

                players.TryGetValue(victim.SoldierName, out vpinfo);
                players.TryGetValue(killer.SoldierName, out kpinfo);

                // ignore event, no web stats available
                if ((type.Equals(BaseEvent.Suicide) && vpinfo == null))
                    return;

                else if ((type.Equals(BaseEvent.Kill) || type.Equals(BaseEvent.TeamKill)) &&
                        (kpinfo == null || vpinfo == null))
                    return;

                // ignore event if server info is not yet available
                if (serverInfo == null)
                    return;

                UpdateStats(kpinfo, vpinfo, type, info, ":" + info.DamageType);

                evaluateLimitsForEvent(type, null, kpinfo, vpinfo, info);

            }
            catch (Exception e)
            {
                DumpException(e);
            }
        }

        public String getServerNameSync()
        {

            server_name_handle.Reset();
            getServerName();
            Thread.Sleep(500);
            WaitOn("server_name_handle", server_name_handle);
            server_name_handle.Reset();

            return this.server_name;
        }

        public void WaitOn(String name, EventWaitHandle handle)
        {
            Int32 timeout = getIntegerVarValue("wait_timeout");

            DebugWrite("waiting, timeout after " + timeout + " seconds", 7);
            if (handle.WaitOne(timeout * 1000) == false)
            {
                StackTrace stack = new StackTrace();
                String caller = stack.GetFrame(1).GetMethod().Name;
                DebugWrite("^1^bWARNING^n^0: Timeout(" + timeout + " seconds), waiting for " + name + " in " + caller + ". Your net connection to your game server may be congested or another plugin may be lagging Procon.", 4);
            }
            else
            {
                DebugWrite("awake! no timeout", 7);
            }
        }

        public String getServerDescriptionSync()
        {
            server_desc_handle.Reset();
            getServerDescription();
            Thread.Sleep(500);
            WaitOn("server_desc_handle", server_desc_handle);
            server_desc_handle.Reset();

            return this.server_desc;
        }

        public void getServerName()
        {
            this.ServerCommand("vars.serverName");
        }

        public void getServerDescription()
        {
            this.ServerCommand("vars.serverDescription");
        }

        public override void OnServerName(String name)
        {
            DebugWrite("Got ^bOnServerName^n!", 8);
            if (!plugin_activated)
                return;

            this.server_name = name;
            this.server_name_handle.Set();
        }

        public override void OnServerDescription(String desc)
        {
            DebugWrite("Got ^bOnServerDescription^n!", 8);
            if (!plugin_activated)
                return;

            this.server_desc = desc;
            this.server_desc_handle.Set();
        }

        public override void OnServerInfo(CServerInfo data)
        {
            DebugWrite("Got ^bOnServerInfo^n!", 8);
            if (!plugin_activated)
                return;

            if (this.serverInfo == null)
                this.serverInfo = new ServerInfo(this, data, this.mapList, new Int32[] { this.curMapIndex, this.nextMapIndex });
            else
                this.serverInfo.updateData(data);

            info_handle.Set();
        }

        /* Always request the map information, whenever there is map-related event */
        public override void OnMaplistLoad() { getMapInfo(); }
        public override void OnMaplistSave() { getMapInfo(); }
        public override void OnMaplistCleared() { getMapInfo(); }
        public override void OnMaplistMapAppended(String mapFileName) { getMapInfo(); }
        public override void OnMaplistNextLevelIndex(Int32 mapIndex) { getMapInfo(); }
        public override void OnMaplistMapRemoved(Int32 mapIndex) { getMapInfo(); }
        public override void OnMaplistMapInserted(Int32 mapIndex, String mapFileName) { getMapInfo(); }
        public override void OnRestartLevel()
        {
            DebugWrite("Got ^bOnRestartLevel^n!", 8);
        }
        public override void OnEndRound(Int32 winTeamId)
        {
            DebugWrite("Got ^bOnEndRound^n!", 8);
            getMapInfo();
        }
        public override void OnRunNextLevel()
        {
            DebugWrite("Got ^bOnRunNextLevel^n!", 8);
            getMapInfo();
        }
        public override void OnCurrentLevel(String mapFileName) { getMapInfo(); }
        public override void OnLoadingLevel(String mapFileName, Int32 roundsPlayed, Int32 roundsTotal) { getMapInfo(); }
        public override void OnLevelStarted()
        {
            DebugWrite("Got ^bOnLevelStarted^n!", 8);
            getMapInfo();
        }
        public override void OnLevelLoaded(String mapFileName, String Gamemode, Int32 roundsPlayed, Int32 roundsTotal)
        {
            DebugWrite("Got ^bOnLevelLoaded^n!", 8);
            level_loaded = true;
            getMapInfo();

            lock (moves_mutex)
            {
                RecentMove.Clear();
            }

            if (!round_over)
            {
                DebugWrite("^bRound was aborted, eval OnRoundOver limits", 4);
                evaluateLimitsForEvent(BaseEvent.RoundOver, null, null, null, null);
                DebugWrite(":::::::::::: Marking round as over (level loaded) :::::::::::: ", 4);
                round_over = true;
                if (!isRoundReset)
                {
                    // Do all of the essential stuff that would happen at normal round end
                    DebugWrite("^bDo reset OnLevelLoaded also", 4);
                    RoundOverReset(); // sets flag to true
                }
            }
        }

        public override void OnMaplistList(List<MaplistEntry> lstMaplist)
        {
            DebugWrite("Got ^bOnMaplistList^n!", 8);
            if (!plugin_activated)
                return;

            this.mapList = lstMaplist;

            if (this.serverInfo != null)
                this.serverInfo.updateMapList(lstMaplist);

            this.list_handle.Set();

        }

        public override void OnRoundOverPlayers(List<CPlayerInfo> players)
        {
            DebugWrite("Got ^bOnRoundOverPlayers^n!", 8);
            if (!plugin_activated)
                return;

            updateQueues(players);
            SyncPlayersList(players);
        }

        public override void OnRoundOverTeamScores(List<TeamScore> teamScores)
        {
            DebugWrite("Got ^bOnRoundOverTeamScores^n!", 8);
            if (!plugin_activated)
                return;

            if (serverInfo == null)
                return;

            serverInfo.updateTickets(teamScores);
            evaluateLimitsForEvent(BaseEvent.RoundOver, null, null, null, null);
            serverInfo.updateTickets(null);
            DebugWrite("::::::::::::  Marking round as over (round over team scores)! :::::::::::: ", 4);
            round_over = true;

            RoundOverReset();
        }

        public override void OnRoundOver(Int32 winTeamId)
        {
            DebugWrite("Got ^bOnRoundOver^n!", 8);
            if (!plugin_activated)
                return;
            level_loaded = false;

            if (serverInfo != null)
                serverInfo.WinTeamId = winTeamId;

        }

        public void RoundOverReset()
        {
            DebugWrite("RoundOverReset called!", 8);
            // reset the activations, and sprees, and round-data for all limits
            List<String> keys = new List<String>(limits.Keys);
            foreach (String key in keys)
                if (limits.ContainsKey(key))
                {
                    limits[key].AccumulateActivations();
                    limits[key].ResetActivations();
                    limits[key].ResetSprees();
                    limits[key].RoundData.Clear();
                }

            // accumlate the round stats for server
            if (serverInfo != null)
            {
                serverInfo.AccumulateRoundStats();
                serverInfo.ResetRoundStats();
                serverInfo.RoundData.Clear();

                //Reset the total limit activations every 10 rounds, so that memory does not grow infinitely
                foreach (KeyValuePair<String, Limit> pair in limits)
                    if (pair.Value != null && (serverInfo.RoundsTotal % 10) == 0)
                        pair.Value.ResetActivationsTotal();
            }

            // accumulate the round stats for players
            foreach (KeyValuePair<String, PlayerInfo> pair in players)
                if (pair.Value != null)
                {
                    pair.Value.AccumulateRoundStats();
                    pair.Value.ResetRoundStats();
                    pair.Value.RoundData.Clear();
                }

            this.RoundData.Clear();
            lockedSquads.Clear();
            squadLeaders.Clear();

            DebugWrite("Round HAS BEEN reset!", 8);
            isRoundReset = true;
        }

        public void getMapList()
        {
            ServerCommand("mapList.list");
        }

        public List<MaplistEntry> getMapListSync()
        {
            list_handle.Reset();
            getMapList();
            Thread.Sleep(500);
            WaitOn("list_handle", list_handle);
            list_handle.Reset();

            return mapList;
        }

        public override void OnMaplistGetMapIndices(Int32 mapIndex, Int32 nextIndex)
        {
            DebugWrite("Got ^bOnMaplistGetMapIndices^n!", 8);
            if (!plugin_activated)
                return;

            this.curMapIndex = mapIndex;
            this.nextMapIndex = nextIndex;

            if (this.serverInfo != null)
                this.serverInfo.updateIndices(new Int32[] { this.curMapIndex, this.nextMapIndex });

            this.indices_handle.Set();
        }

        public void getMapIndices()
        {
            ServerCommand("mapList.getMapIndices");
        }

        public void getModeCounters()
        {
            ServerCommand("vars.gameModeCounter");
            if (game_version == "BF3")
                ServerCommand("vars.ctfRoundTimeModifier");
        }

        public Int32[] getMapIndicesSync()
        {
            indices_handle.Reset();
            getMapIndices();
            Thread.Sleep(500);
            WaitOn("indices_handle", indices_handle);
            indices_handle.Reset();

            return new Int32[] { curMapIndex, nextMapIndex };
        }

        public void getMapInfoSync()
        {
            getModeCounters();
            DebugWrite("waiting for map-list before proceeding", 8);
            getMapListSync();
            DebugWrite("waiting for map-indices before proceeding", 8);
            getMapIndicesSync();
            DebugWrite("waiting for server-info before proceeding", 8);
            getServerInfoSync();
        }

        public void getMapInfo()
        {
            if (!plugin_activated)
                return;

            getModeCounters();
            getMapList();
            getMapIndices();
            getServerInfo();
            getModeCounters();
        }

        public void updateVars()
        {
            if (!plugin_activated) return;

            if (game_version == "BF3")
            {
                ServerCommand("vars.bulletDamage");
                ServerCommand("vars.friendlyFire");
                ServerCommand("vars.gunMasterWeaponsPreset");
                ServerCommand("vars.idleTimeout");
                ServerCommand("vars.soldierHealth");
                ServerCommand("vars.vehicleSpawnAllowed");
                ServerCommand("vars.vehicleSpawnDelay");
            }
            else
            {
                ServerCommand("vars.bulletDamage");
                ServerCommand("vars.friendlyFire");
                ServerCommand("vars.idleTimeout");
                ServerCommand("vars.soldierHealth");
                ServerCommand("vars.vehicleSpawnAllowed");
                ServerCommand("vars.vehicleSpawnDelay");
                if (game_version == "BF4")
                    ServerCommand("vars.commander");
                else if (game_version == "BFHL")
                    ServerCommand("vars.hacker");
                ServerCommand("vars.serverType");
                ServerCommand("vars.maxSpectators");
                if (game_version == "BF4")
                    ServerCommand("vars.teamFactionOverride");
            }

            resetUpdateTimer(WhichTimer.Vars);
        }

        public override void OnReservedSlotsList(List<String> lstSoldierNames)
        {
            DebugWrite("Got ^bOnReservedSlotsList^n!", 8);
            reserved_slots_list = lstSoldierNames;
        }

        public Boolean stringValidator(String var, String value)
        {
            try
            {
                if (var.Equals("console"))
                    PluginCommand(null, value);
                else if (var.Equals("new_limit") && value.Equals(TrueFalse.True.ToString()))
                    createNewLimit();
                else if (var.Equals("new_list") && value.Equals(TrueFalse.True.ToString()))
                    createNewList();
                else if (var.Equals("twitter_verifier_pin") && !value.Equals(default_PIN_message))
                    VerifyTwitterPin(value);
                else if (var.Equals("compile_limit") && value.Equals(LimitChoice.NotCompiled.ToString()))
                    CompileAll();
                else if (var.Equals("compile_limit") && value.Equals(LimitChoice.All.ToString()))
                    CompileAll(true);

            }
            catch (Exception e)
            {
                DumpException(e);
            }

            return false;
        }

        private void PluginCommand(String sender, String cmd)
        {

            try
            {
                //operations

                Match dumpLimitMatch = Regex.Match(cmd, @"\s*[!@/]\s*dump\s+limit\s+(\d+)\s*", RegexOptions.IgnoreCase);
                Match playerStatsMatch = Regex.Match(cmd, @"\s*[!@/]\s*(?:(weapon)\s+)?(web|round|total)\s+stats\s+(.+)\s*", RegexOptions.IgnoreCase);

                Match serverStatsMatch = Regex.Match(cmd, @"\s*[!@/]\s*(?:(weapon)\s+)?(round|total|map)\s+stats\s*", RegexOptions.IgnoreCase);

                //Setting/Getting keys
                Match setVarValueMatch = Regex.Match(cmd, @"\s*[!@/]\s*set\s+([^ ]+)\s+(.+)", RegexOptions.IgnoreCase);
                Match setVarValueEqMatch = Regex.Match(cmd, @"\s*[!@/]\s*set\s+([^ ]+)\s*=\s*(.+)", RegexOptions.IgnoreCase);
                Match setVarValueToMatch = Regex.Match(cmd, @"\s*[!@/]\s*set\s+([^ ]+)\s+to\s+(.+)", RegexOptions.IgnoreCase);
                Match setVarTrueMatch = Regex.Match(cmd, @"\s*[!@/]\s*set\s+([^ ]+)", RegexOptions.IgnoreCase);
                Match getVarValueMatch = Regex.Match(cmd, @"\s*[!@/]\s*get\s+([^ ]+)", RegexOptions.IgnoreCase);
                Match enableMatch = Regex.Match(cmd, @"\s*[!@/]\s*enable\s+(.+)", RegexOptions.IgnoreCase);
                Match disableMatch = Regex.Match(cmd, @"\s*[!@/]\s*disable\s+(.+)", RegexOptions.IgnoreCase);

                //Information
                Match pluginSettingsMatch = Regex.Match(cmd, @"\s*[!@/]\s*settings", RegexOptions.IgnoreCase);

                Boolean senderIsAdmin = true;

                if (playerStatsMatch.Success && senderIsAdmin)
                    playerStatsDumpCmd(sender, playerStatsMatch.Groups[1].Value, playerStatsMatch.Groups[2].Value, playerStatsMatch.Groups[3].Value);
                else if (serverStatsMatch.Success)
                    serverStatsDumpCmd(sender, serverStatsMatch.Groups[1].Value, serverStatsMatch.Groups[2].Value);
                else if (dumpLimitMatch.Success && senderIsAdmin)
                    dumpLimitCmd(sender, dumpLimitMatch.Groups[1].Value);
                else if (setVarValueEqMatch.Success && senderIsAdmin)
                    setVariableCmd(sender, setVarValueEqMatch.Groups[1].Value, setVarValueEqMatch.Groups[2].Value);
                else if (setVarValueToMatch.Success && senderIsAdmin)
                    setVariableCmd(sender, setVarValueToMatch.Groups[1].Value, setVarValueToMatch.Groups[2].Value);
                else if (setVarValueMatch.Success && senderIsAdmin)
                    setVariableCmd(sender, setVarValueMatch.Groups[1].Value, setVarValueMatch.Groups[2].Value);
                else if (setVarTrueMatch.Success && senderIsAdmin)
                    setVariableCmd(sender, setVarTrueMatch.Groups[1].Value, "1");
                else if (getVarValueMatch.Success && senderIsAdmin)
                    getVariableCmd(sender, getVarValueMatch.Groups[1].Value);
                else if (enableMatch.Success && senderIsAdmin)
                    enableVarGroupCmd(sender, enableMatch.Groups[1].Value);
                else if (disableMatch.Success && senderIsAdmin)
                    disableVarGroupCmd(sender, disableMatch.Groups[1].Value);
            }
            catch (Exception e)
            {
                DumpException(e);
            }
        }

        // modified algorithm to ignore insertions, and case
        public static Int32 LevenshteinDistance(String s, String t)
        {
            s = s.ToLower();
            t = t.ToLower();

            Int32 n = s.Length;
            Int32 m = t.Length;
            Int32[,] d = new Int32[n + 1, m + 1];

            if (n == 0)
                return m;

            if (m == 0)
                return n;

            for (Int32 i = 0; i <= n; d[i, 0] = i++) ;
            for (Int32 j = 0; j <= m; d[0, j] = j++) ;

            for (Int32 i = 1; i <= n; i++)
                for (Int32 j = 1; j <= m; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 0), d[i - 1, j - 1] + ((t[j - 1] == s[i - 1]) ? 0 : 1));

            return d[n, m];
        }

        public String bestMatch(String name, List<String> names, out Int32 best_distance)
        {
            return bestMatch(name, names, out best_distance, true);
        }

        //find the best match for name in names
        public String bestMatch(String name, List<String> names, out Int32 best_distance, Boolean fuzzy)
        {

            best_distance = Int32.MaxValue;

            try
            {

                //do the obvious check first
                if (names.Contains(name))
                {
                    best_distance = 0;
                    return name;
                }

                //name is not in the list, find the best match
                String best_match = null;

                // first try to see if any of the names contains target name as substring, so we can reduce the search
                Dictionary<String, String> sub_names = new Dictionary<String, String>();

                String name_lower = name.ToLower();

                for (Int32 i = 0; i < names.Count; i++)
                {
                    String cname = names[i].ToLower();
                    if (cname.Equals(name_lower))
                        return names[i];
                    else if (cname.Contains(name_lower) && !sub_names.ContainsKey(cname))
                        sub_names.Add(cname, names[i]);
                }

                if (sub_names.Count > 0)
                    names = new List<String>(sub_names.Keys);

                if (sub_names.Count == 1)
                {
                    // we can optimize, and exit early
                    best_match = sub_names[names[0]];
                    best_distance = Math.Abs(best_match.Length - name.Length);
                    return best_match;
                }

                // if we are not doing a fuzzy search, and we have not found more than one sub-string, we can exit here
                if (!fuzzy && sub_names.Count == 0)
                    return null;

                // find the best/fuzzy match using modified Leveshtein algorithm
                foreach (String cname in names)
                {
                    Int32 distance = LevenshteinDistance(name, cname);
                    if (distance < best_distance)
                    {
                        best_distance = distance;
                        best_match = cname;
                    }
                }

                if (best_match == null)
                    return null;

                best_distance += Math.Abs(name.Length - best_match.Length);

                // if we searched through sub-names, get the actual match
                if (sub_names.Count > 0 && sub_names.ContainsKey(best_match))
                    best_match = sub_names[best_match];

                return best_match;
            }
            catch (Exception e)
            {
                DumpException(e);
            }

            return null;
        }

        public String BestPlayerMatch(String name)
        {
            if (name == null || name.Trim().Length == 0)
                return null;

            return bestPlayerMatch(null, name, true, true);
        }

        public String bestPlayerMatch(String sender, String player, Boolean fuzzy, Boolean quiet)
        {

            try
            {
                if (player == null)
                    return null;
                if (players.ContainsKey(player))
                    return player;

                Int32 edit_distance = 0;
                String new_player = null;
                if ((new_player = bestMatch(player, new List<String>(players.Keys), out edit_distance, fuzzy)) == null)
                {
                    if (!quiet)
                        SendConsoleWarning(sender, "could not find ^b" + player + "^n");
                    return null;
                }
                if (!quiet)
                    SendConsoleWarning(sender, "could not find ^b" + player + "^n, but found ^b" + new_player + "^n, with edit distance of ^b" + edit_distance + "^n");
                return new_player;
            }
            catch (Exception e)
            {
                DumpException(e);
            }

            return null;
        }

        private void serverStatsDumpCmd(String sender, String level, String scope)
        {
            if (serverInfo == null)
                return;

            if (level == null)
                level = String.Empty;

            if (scope == null)
                scope = String.Empty;

            if (!(scope.Equals("round") || scope.Equals("total") || scope.Equals("map")))
            {
                SendConsoleError(sender, "unknown stats scope ^b" + scope + "^n");
                return;
            }

            if (level.Equals("weapon"))
                serverInfo.dumpWeaponStats(scope);
            else
                serverInfo.dumpStatProperties(scope);

        }

        private void playerStatsDumpCmd(String sender, String level, String scope, String player)
        {
            if (player == null || player.Trim().Length == 0 || players.Count == 0)
                return;

            player = player.Trim();

            if ((player = bestPlayerMatch(sender, player, true, false)) == null)
                return;

            if (level == null)
                level = String.Empty;

            if (scope == null)
                scope = String.Empty;

            PlayerInfo pinfo = players[player];

            if (!(scope.Equals("round") || scope.Equals("total") || scope.Equals("web")))
            {
                SendConsoleError(sender, "unknown stats scope ^b" + scope + "^n");
                return;
            }

            if (level.Equals("weapon"))
                pinfo.dumpWeaponStats(scope);
            else
                pinfo.dumpStatProperties(scope);

        }

        private void dumpLimitCmd(String sender, String id)
        {
            id = id.Trim();
            if (!limits.ContainsKey(id))
            {
                SendConsoleError(sender, "Limit #^b" + id + "^n  does not exist");
                return;
            }

            Limit limit = limits[id];
            String class_source = buildLimitSource(limit);
            String path = getClassName(limit) + ".cs";
            SendConsoleMessage(sender, "Dumping " + limit.ShortDisplayName + " source to file " + path);
            DumpData(class_source, path);
        }

        private void setVariableCmd(String sender, String var, String val)
        {
            if (setPluginVarValue(sender, var, val))
                SendConsoleMessage(sender, var + " set to \"" + val + "\"");
        }

        private void getVariableCmd(String sender, String var)
        {
            String val = getPluginVarValue(sender, var);

            SendConsoleMessage(sender, var + " = " + val);

        }

        private void enableVarGroupCmd(String sender, String group)
        {
            if (group.CompareTo("plugin") == 0)
            {
                ConsoleWrite("Disabling plugin");
                this.ExecuteCommand("procon.plugin.enable", "InsaneLimits", "false");
                return;
            }
            enablePluginVarGroup(sender, group);
        }

        private void disableVarGroupCmd(String sender, String group)
        {
            if (group.CompareTo("plugin") == 0)
            {
                ConsoleWrite("Enabling plugin");
                this.ExecuteCommand("procon.plugin.enable", "InsaneLimits", "true");
                return;
            }

            disablePluginVarGroup(sender, group);
        }

        private Boolean enablePluginVarGroup(String sender, String group)
        {
            // search for all variable matching
            List<String> vars = getVariableNames(group);
            if (vars.Count == 0)
            {
                SendConsoleError(sender, "no variables match \"" + group + "\"");
                return false;
            }

            return setPluginVarGroup(sender, String.Join(",", vars.ToArray()), "true");
        }

        private Boolean disablePluginVarGroup(String sender, String group)
        {
            //search for all variables matching
            List<String> vars = getVariableNames(group);

            if (vars.Count == 0)
            {
                SendConsoleError(sender, "no variables match \"" + group + "\"");
                return false;
            }
            return setPluginVarGroup(sender, String.Join(",", vars.ToArray()), "false");
        }

        private Boolean setPluginVarGroup(String sender, String group, String val)
        {

            if (group == null)
            {
                SendConsoleError(sender, "no variable to enable");
                return false;
            }

            group = group.Replace(";", ",");
            List<String> vars = new List<String>(Regex.Split(group, @"\s*,\s*", RegexOptions.IgnoreCase));
            foreach (String var in vars)
                if (setPluginVarValue(sender, var, val))
                    SendConsoleMessage(sender, var + " set to \"" + val + "\"");

            return true;
        }

        private List<String> getVariableNames(String group)
        {
            List<String> names = new List<String>();
            List<String> list = new List<String>(Regex.Split(group, @"\s*,\s*"));
            List<String> vars = getPluginVars();
            foreach (String search in list)
            {
                foreach (String var in vars)
                {
                    if (var.Contains(search))
                        if (!names.Contains(var))
                            names.Add(var);
                }
            }

            return names;
        }

        public String StripModifiers(String text)
        {
            return Regex.Replace(text, @"\^[0-9a-zA-Z]", "");
        }

        private void SendConsoleMessage(String name, String msg, MessageType type)
        {

            msg = FormatMessage(msg, type);
            LogWrite(msg);

            // remove font style
            msg = StripModifiers(E(msg));

            if (name != null)
                SendPlayerMessageV(name, msg);
        }

        private void SendConsoleMessage(String name, String msg)
        {
            SendConsoleMessage(name, msg, MessageType.Normal);
        }

        private void SendConsoleError(String name, String msg)
        {
            SendConsoleMessage(name, msg, MessageType.Error);
        }

        private void SendConsoleWarning(String name, String msg)
        {
            SendConsoleMessage(name, msg, MessageType.Warning);
        }

        private void SendConsoleException(String name, String msg)
        {
            SendConsoleMessage(name, msg, MessageType.Exception);
        }

        private void SendPlayerMessageV(String name, String message)
        {
            if (name == null)
                return;

            ServerCommand("admin.say", StripModifiers(E(message)), "player", name);
        }

        private Boolean SendGlobalMessageV(String message)
        {
            ServerCommand("admin.say", StripModifiers(E(message)), "all");
            return true;
        }

        private Boolean SendTeamMessageV(Int32 teamId, String message)
        {
            ServerCommand("admin.say", StripModifiers(E(message)), "team", (teamId).ToString());
            return true;
        }

        private Boolean SendSquadMessageV(Int32 teamId, Int32 squadId, String message)
        {
            ServerCommand("admin.say", StripModifiers(E(message)), "squad", (teamId).ToString(), (squadId).ToString());
            return true;
        }

        private void SendPlayerYellV(String name, String message, Int32 duration)
        {
            if (name == null)
                return;

            ServerCommand("admin.yell", StripModifiers(E(message)), duration.ToString(), "player", name);
        }

        private Boolean SendGlobalYellV(String message, Int32 duration)
        {
            ServerCommand("admin.yell", StripModifiers(E(message)), duration.ToString(), "all");
            return true;
        }

        private Boolean SendTeamYellV(Int32 teamId, String message, Int32 duration)
        {
            ServerCommand("admin.yell", StripModifiers(E(message)), duration.ToString(), "team", (teamId).ToString());
            return true;
        }

        //escape replacements
        public String E(String text)
        {
            text = Regex.Replace(text, @"\\n", "\n");
            text = Regex.Replace(text, @"\\t", "\t");
            return text;
        }

        /* Messaging functions (Check for Virtual Mode) */

        public Boolean SendGlobalMessage(String message)
        {
            return SendGlobalMessage(message, 0);
        }

        public Boolean SendGlobalMessage(String message, Int32 delay)
        {
            if (VMode)
            {
                ConsoleWarn("not sending global-message, ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            if (delay == 0)
            {
                SendGlobalMessageV(message);
                return true;
            }

            Thread delayed = new Thread(new ThreadStart(delegate ()
            {
                Thread.Sleep(delay * 1000);
                QueueSayMessage(new SayMessage(0, 0, String.Empty, MessageAudience.All, message));
            }));

            delayed.IsBackground = true;
            delayed.Name = "msg_delay";
            delayed.Start();

            return true;
        }

        public Boolean SendTeamMessage(Int32 teamId, String message)
        {
            return SendTeamMessage(teamId, message, 0);
        }

        public Boolean SendTeamMessage(Int32 teamId, String message, Int32 delay)
        {
            if (VMode)
            {
                ConsoleWarn("not sending team-message to TeamId(^b" + teamId + "^n,), ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            if (delay == 0)
            {
                SendTeamMessageV(teamId, message);
                return true;
            }

            Thread delayed = new Thread(new ThreadStart(delegate ()
            {
                Thread.Sleep(delay * 1000);
                QueueSayMessage(new SayMessage(teamId, 0, String.Empty, MessageAudience.Team, message));
            }));

            delayed.IsBackground = true;
            delayed.Name = "team_msg_delay";
            delayed.Start();

            return true;
        }

        public Boolean SendSquadMessage(Int32 teamId, Int32 squadId, String message)
        {
            return SendSquadMessage(teamId, squadId, message, 0);
        }

        public Boolean SendSquadMessage(Int32 teamId, Int32 squadId, String message, Int32 delay)
        {
            if (VMode)
            {
                ConsoleWarn("not sending squad-message to TeamId(^b" + teamId + "^n,).SquadId(^b" + squadId + "^n), ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            if (delay == 0)
            {
                SendSquadMessageV(teamId, squadId, message);
                return true;
            }

            Thread delayed = new Thread(new ThreadStart(delegate ()
            {
                Thread.Sleep(delay * 1000);
                QueueSayMessage(new SayMessage(teamId, squadId, String.Empty, MessageAudience.Squad, message));
            }));

            delayed.IsBackground = true;
            delayed.Name = "squad_msg_delay";
            delayed.Start();

            return true;
        }

        public Boolean SendPlayerMessage(String name, String message)
        {
            return SendPlayerMessage(name, message, 0);
        }

        public Boolean SendPlayerMessage(String name, String message, Int32 delay)
        {
            if (VMode)
            {
                ConsoleWarn("not sending player-message to ^b" + name + "^n, ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            if (delay == 0)
            {
                SendPlayerMessageV(name, message);
                return true;
            }

            Thread delayed = new Thread(new ThreadStart(delegate ()
            {
                Thread.Sleep(delay * 1000);
                QueueSayMessage(new SayMessage(0, 0, name, MessageAudience.Player, message));
            }));

            delayed.IsBackground = true;
            delayed.Name = "player_msg_delay";
            delayed.Start();

            return true;
        }

        public Boolean SendPlayerYell(String name, String message, Int32 duration)
        {
            if (name == null) return false;

            if (VMode)
            {
                ConsoleWarn("not yelling player-message to ^b" + name + "^n, ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            SendPlayerYellV(name, message, duration);
            return true;
        }

        public Boolean SendGlobalYell(String message, Int32 duration)
        {
            if (VMode)
            {
                ConsoleWarn("not yelling global-message, ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            SendGlobalYellV(message, duration);
            return true;
        }

        public Boolean SendTeamYell(Int32 teamId, String message, Int32 duration)
        {
            if (VMode)
            {
                ConsoleWarn("not yelling team-message, ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            SendTeamYellV(teamId, message, duration);
            return true;
        }

        public Boolean VMode
        {
            get
            {
                if (getBooleanVarValue("virtual_mode"))
                    return true;

                if (VModeSlot == null)
                    return false;

                Object mode = Thread.GetData(VModeSlot);
                if (mode == null || !mode.GetType().Equals(typeof(Boolean)))
                    return false;

                return (Boolean)mode;
            }
        }

        List<String> scratch_list = new List<String>();

        public void updateQueues(List<CPlayerInfo> lstPlayers)
        {
            DebugWrite("OnListPlayers::updateQueues locking, " + GetQCount() + " in queue", 8);
            lock (players_mutex)
            {
                scratch_handle.Reset();
                // update the scratch list
                scratch_list.Clear();
                foreach (CPlayerInfo info in lstPlayers)
                    if (!scratch_list.Contains(info.SoldierName))
                        scratch_list.Add(info.SoldierName);

                // make a list of players to drop from the stats queue
                List<String> players_to_remove = new List<String>();
                foreach (KeyValuePair<String, CPunkbusterInfo> pair in new_player_queue)
                    if (!scratch_list.Contains(pair.Key) && !players_to_remove.Contains(pair.Key))
                        players_to_remove.Add(pair.Key);

                // now actually drop them from the new players queue
                foreach (String name in players_to_remove)
                    if (new_player_queue.ContainsKey(name))
                    {
                        DebugWrite("Looks like ^b" + name + "^n left the server, removing him from stats queue", 5);
                        new_player_queue.Remove(name);
                    }

                // make a list of players to drop from the new players batch
                players_to_remove.Clear();
                foreach (KeyValuePair<String, PlayerInfo> pair in new_players_batch)
                    if (!scratch_list.Contains(pair.Key) && !players_to_remove.Contains(pair.Key))
                        players_to_remove.Add(pair.Key);

                // now actually drop them from the new players batch
                foreach (String name in players_to_remove)
                    if (new_players_batch.ContainsKey(name))
                        new_players_batch.Remove(name);
            }
            DebugWrite("OnListPlayers::updateQueues UNLOCKING, " + GetQCount() + " in queue", 8);
            scratch_handle.Set();
        }

        private Int32 SafePlayerCount()
        {
            Int32 num = 0;
            lock (players_mutex)
            {
                num = players.Keys.Count;
            }
            return num;
        }

        public void SyncPlayersList(List<CPlayerInfo> lstPlayers)
        {
            DebugWrite("OnListPlayers::SyncPlayersList locking, " + SafePlayerCount() + " players", 8);
            plist_handle.Reset();
            lock (players_mutex)
            {
                // first update the information that players that still are in list
                foreach (CPlayerInfo cpiPlayer in lstPlayers)
                    if (this.players.ContainsKey(cpiPlayer.SoldierName))
                        this.players[cpiPlayer.SoldierName].updateInfo(cpiPlayer);

                //build a lookup table
                Dictionary<String, Boolean> player_lookup = new Dictionary<String, Boolean>();
                foreach (CPlayerInfo pinfo in lstPlayers)
                    if (!player_lookup.ContainsKey(pinfo.SoldierName))
                        player_lookup.Add(pinfo.SoldierName, true);

                List<String> players_to_remove = new List<String>();

                // now make a list of players that will need to be removed
                foreach (KeyValuePair<String, PlayerInfo> pair in players)
                    if (!player_lookup.ContainsKey(pair.Key) && !players_to_remove.Contains(pair.Key))
                        players_to_remove.Add(pair.Key);

                // now actually remove them
                foreach (String pname in players_to_remove)
                    InnerRemovePlayer(pname);
            }
            DebugWrite("OnListPlayers::SyncPlayersList UNLOCKING, " + SafePlayerCount() + " players", 8);
            plist_handle.Set();
        }

        public void UpdateExtraInfo(List<CPlayerInfo> lstPlayers)
        {
            DateTime ts = DateTime.Now;
            lock (updates_mutex)
            {
                ts = timerSquad;
            }
            Boolean itIsTime = (DateTime.Now.Subtract(ts).TotalSeconds > getFloatVarValue("update_interval"));
            Dictionary<String, Int32> squadCounts = new Dictionary<String, Int32>();
            String key = null;

            DebugWrite("UpdateExtraInfo seconds since last update: " + DateTime.Now.Subtract(ts).TotalSeconds.ToString("F0"), 7);
            if (!itIsTime) return;

            Int32 updatedPing = 0;
            Int32 badPing = 0;

            foreach (CPlayerInfo cpiPlayer in lstPlayers)
            {
                if ((cpiPlayer.Score == 0 || Double.IsNaN(cpiPlayer.Score)) && cpiPlayer.Deaths == 0)
                {
                    DebugWrite("Updating idle duration for: " + cpiPlayer.SoldierName, 6);
                    ServerCommand("player.idleDuration", cpiPlayer.SoldierName); // Update it
                }

                if (cpiPlayer.TeamID > 0 && cpiPlayer.SquadID > 0)
                {
                    key = cpiPlayer.TeamID.ToString() + "/" + cpiPlayer.SquadID;
                    if (!squadCounts.ContainsKey(key))
                    {
                        squadCounts[key] = 1;
                    }
                    else
                    {
                        squadCounts[key] = squadCounts[key] + 1;
                    }
                }

                if (cpiPlayer.Ping > 0 && cpiPlayer.Ping < 65535)
                {
                    OnPlayerPingedByAdmin(cpiPlayer.SoldierName, cpiPlayer.Ping);
                    ++updatedPing;
                }
                else
                {
                    ++badPing;
                }
            }

            if (updatedPing > 0 || badPing > 0)
                DebugWrite("Updated pings: " + updatedPing + " good, " + badPing + " bad, " + lstPlayers.Count + " total", 5);

            String[] ids = null;
            Char[] div = new Char[] { '/' };
            foreach (String k in squadCounts.Keys)
            {
                DebugWrite("Updating squad privacy and leader for: " + k, 6);
                ids = k.Split(div);
                ServerCommand("squad.private", ids[0], ids[1]);
                // Request leader only for squads with more than one player
                if (squadCounts[k] > 1)
                {
                    ServerCommand("squad.leader", ids[0], ids[1]);
                }
            }
            resetUpdateTimer(WhichTimer.Squad);
        }

        public void RemovePlayer(String name)
        {
            DebugWrite("RemovePlayer locking, " + name, 8);
            lock (players_mutex)
            {
                InnerRemovePlayer(name);
            }
            DebugWrite("RemovePlayer UNLOCKING, " + name, 8);
        }

        private void InnerRemovePlayer(String name)
        {
            try
            {
                if (!players.ContainsKey(name))
                    return;

                List<String> lkeys = new List<String>(limits.Keys);

                // for players removed, reset the activations/evaluations
                foreach (String lkey in lkeys)
                    if (limits.ContainsKey(lkey))
                    {
                        limits[lkey].ResetActivationsTotal(name);
                        limits[lkey].ResetActivations(name);
                    }

                players.Remove(name);
            }
            catch (Exception e)
            {
                DumpException(e);
            }
        }

        public override void OnListPlayers(List<CPlayerInfo> lstPlayers, CPlayerSubset cpsSubset)
        {
            DebugWrite("Got ^bOnListPlayers^n!", 8);
            if (!plugin_activated)
                return;

            if (cpsSubset.Subset != CPlayerSubset.PlayerSubsetType.All)
                return;

            updateQueues(lstPlayers);
            SyncPlayersList(lstPlayers);
            UpdateExtraInfo(lstPlayers);
        }

        public Int32 sort_players_t_desc_cmp(String left_name, String right_name)
        {
            PlayerInfo left = null;
            PlayerInfo right = null;

            if (players.ContainsKey(left_name))
                left = players[left_name];

            if (players.ContainsKey(right_name))
                right = players[right_name];

            Int32 result = 0;
            if (left != null && right != null)
                result = left.JoinTime.CompareTo(right.JoinTime);
            else if (left != null && right == null)
                result = 1;
            else if (left == null && right != null)
                result = -1;
            else
                result = 0;

            return result * (-1);

        }

        public Int32 sort_limits_id_asc_cmp(Limit left_limit, Limit right_limit)
        {
            Int32 left = -1; ;
            Int32 right = -1;

            Int32.TryParse(left_limit.id, out left);
            Int32.TryParse(right_limit.id, out right);

            Int32 result = 0;
            if (left != -1 && right != -1)
                result = left.CompareTo(right);
            else if (left != -1 && right == -1)
                result = 1;
            else if (left == -1 && right != -1)
                result = -1;
            else
                result = 0;

            return result;
        }

        public void DumpPlayersList(List<String> sorted_players, Int32 debug_level)
        {
            Int32 i = 0;
            DebugWrite("Sorted player's list: ", debug_level);
            foreach (String name in sorted_players)
            {
                i++;
                PlayerInfo info = null;
                if (players.ContainsKey(name))
                    info = players[name];

                String join_t = (info != null) ? info.JoinTime.ToString() : "Null";
                DebugWrite(i + ". " + name + ", JoinTime: " + join_t, debug_level);
            }
        }

        public List<Limit> getLimitsForEvaluation(Limit.EvaluationType type)
        {
            List<Limit> sorted_limits = new List<Limit>(limits.Values);

            // remove all the invalid limits
            sorted_limits.RemoveAll(delegate (Limit limit)
            {
                return limit.Invalid || (type & limit.Evaluation) != limit.Evaluation;
            });

            // sort the remaining valid limits
            sorted_limits.Sort(sort_limits_id_asc_cmp);

            return sorted_limits;
        }

    }
}
