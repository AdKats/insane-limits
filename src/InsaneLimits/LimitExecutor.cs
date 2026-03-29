using System;
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
        public String R(String message)
        {
            if (ReplacementsDict == null)
                return message;

            foreach (KeyValuePair<String, String> pair in ReplacementsDict)
                if (message.Contains(pair.Key))
                    message = message.Replace(pair.Key, pair.Value);

            if (AdvancedReplacementsDict == null)
                return message;

            /*
                        foreach (KeyValuePair<String, String> pair in AdvancedReplacementsDict)
                            if (message.Contains(pair.Key))
                                message = message.Replace(pair.Key, pair.Value);
            */

            // Ensure correct match for aliased substrings
            // Battlelog404 is the only property that ends with a digit
            // Use [^A-Za-z] as the terminator of the prop name

            Match m = null;
            String ds = message;

            foreach (KeyValuePair<String, String> pair in AdvancedReplacementsDict)
            {
                while ((m = Regex.Match(message, pair.Key + @"(?:[^A-Za-z]|$)")).Success)
                {
                    if (getIntegerVarValue("debug_level") >= 1) ds = message.Insert(m.Index, "^b").Insert(m.Index + pair.Key.Length + 2, "^n");
                    DebugWrite("Replacing " + pair.Key + ": " + ds, 6);
                    message = message.Replace(pair.Key, pair.Value);
                }
            }

            DebugWrite("New repl: " + message, 6);

            return message;

        }

        public void SetupReplacements(Limit limit, PlayerInfoInterface player, PlayerInfoInterface killer, KillInfoInterface kill, PlayerInfoInterface victim)
        {


            //Re-Adjust the targets, depending on the event type, so that no variables are NULL
            // Legend
            // e - event
            // k - kill
            // d - death
            // p - player
            // s - suicide
            // g - group
            // How to Read, kd_e, Kill-Death Event

            Limit.EvaluationType kd_e = Limit.EvaluationType.OnKill | Limit.EvaluationType.OnDeath | Limit.EvaluationType.OnTeamKill | Limit.EvaluationType.OnTeamDeath;

            Limit.EvaluationType k_e = Limit.EvaluationType.OnKill | Limit.EvaluationType.OnTeamKill;

            Limit.EvaluationType d_e = Limit.EvaluationType.OnDeath | Limit.EvaluationType.OnTeamDeath;

            Limit.EvaluationType p_e = Limit.EvaluationType.OnJoin | Limit.EvaluationType.OnLeave | Limit.EvaluationType.OnSpawn | Limit.EvaluationType.OnInterval | Limit.EvaluationType.OnIntervalPlayers | Limit.EvaluationType.OnAnyChat | Limit.EvaluationType.OnTeamChange;
            /*Limit.EvaluationType.OnGlobalChat | Limit.EvaluationType.OnSquadChat | Limit.EvaluationType.OnTeamChat*/

            Limit.EvaluationType s_e = Limit.EvaluationType.OnSuicide;
            Limit.EvaluationType g_e = Limit.EvaluationType.OnRoundOver | Limit.EvaluationType.OnRoundStart | Limit.EvaluationType.OnIntervalServer;

            if ((limit.Evaluation & p_e) > 0 && player != null)
            {
                killer = player;
                victim = player;
                CPlayerInfo dummy = new CPlayerInfo(player.Name, player.Tag, player.TeamId, player.SquadId);
                kill = new KillInfo(new Kill(dummy, dummy, "UnkownWeapon", false, new Point3D(), new Point3D()), BaseEvent.Kill, "None");
            }
            else if ((limit.Evaluation & kd_e) > 0 && kill != null)
            {
                if ((limit.Evaluation & k_e) > 0 && killer != null)
                    player = killer;
                else if ((limit.Evaluation & d_e) > 0 && victim != null)
                    player = victim;
            }
            else if ((limit.Evaluation & s_e) > 0 && player != null)
            {
                killer = player;
                victim = player;
            }
            else if ((limit.Evaluation & g_e) > 0)
            {
                //None of the replacements apply (try/catch takes care of it)
                killer = null;
                victim = null;
                player = null;
                kill = null;
            }

            // use a shorted refernece, lazy
            Dictionary<String, String> dict = ReplacementsDict;
            Double value = 0;
            List<String> keys = new List<String>(ReplacementsDict.Keys);

            foreach (String key in keys)
            {
                try
                {
                    switch (key)
                    {
                        // Killer Replacements (Evaluations:  OnKill, OnDeath, OnTeamKills, and OnTeamDeath)
                        case "%k_n%":
                            dict[key] = killer.Name;
                            break;
                        case "%k_ct%":
                            dict[key] = killer.Tag;
                            break;
                        case "%k_cn%":
                            dict[key] = killer.CountryName;
                            break;
                        case "%k_cc%":
                            dict[key] = killer.CountryCode;
                            break;
                        case "%k_ip%":
                            dict[key] = killer.IPAddress;
                            break;
                        case "%k_eg%":
                            dict[key] = killer.EAGuid;
                            break;
                        case "%k_pg%":
                            dict[key] = killer.PBGuid;
                            break;
                        case "%k_fn%":
                            dict[key] = killer.FullName;
                            break;

                        // Victim Replacements (Evaluations:  OnKill, OnDeath, OnTeamKills, and OnTeamDeath)
                        case "%v_n%":
                            dict[key] = victim.Name;
                            break;
                        case "%v_ct%":
                            dict[key] = victim.Tag;
                            break;
                        case "%v_cn%":
                            dict[key] = victim.CountryName;
                            break;
                        case "%v_cc%":
                            dict[key] = victim.CountryCode;
                            break;
                        case "%v_ip%":
                            dict[key] = victim.IPAddress;
                            break;
                        case "%v_eg%":
                            dict[key] = victim.EAGuid;
                            break;
                        case "%v_pg%":
                            dict[key] = victim.PBGuid;
                            break;
                        case "%v_fn%":
                            dict[key] = victim.FullName;
                            break;


                        // Player Repalcements (Evaluations: OnJoin, OnLeave, OnSpawn, OnAnyChat, OnTeamChange, and OnSuicide)
                        case "%p_n%":
                            dict[key] = player.Name;
                            break;
                        case "%p_ct%":
                            dict[key] = player.Tag;
                            break;
                        case "%p_cn%":
                            dict[key] = player.CountryName;
                            break;
                        case "%p_cc%":
                            dict[key] = player.CountryCode;
                            break;
                        case "%p_ip%":
                            dict[key] = player.IPAddress;
                            break;
                        case "%p_eg%":
                            dict[key] = player.EAGuid;
                            break;
                        case "%p_pg%":
                            dict[key] = player.PBGuid;
                            break;
                        case "%p_fn%":
                            dict[key] = player.FullName;
                            break;
                        case "%p_lc%":
                            dict[key] = player.LastChat;
                            break;

                        // Weapon Replacements (Evaluations: OnKill, OnDeath, OnTeamKill, OnTeamDeath, and OnSuicide)
                        case "%w_n%":
                            //dict[key] = kill.Weapon;
                            {
                                KillReasonInterface kr = FriendlyWeaponName(kill.Weapon);
                                dict[key] = kr.Name;
                                if (kr.Name == "Death" && !String.IsNullOrEmpty(kr.VehicleName))
                                    dict[key] = kr.VehicleName;
                            }
                            break;
                        case "%w_p_x%":
                            dict[key] = killer[kill.Weapon].KillsRound.ToString();
                            break;
                        case "%w_a_x%":
                            dict[key] = (serverInfo == null) ? key : serverInfo[kill.Weapon].KillsRound.ToString();
                            break;

                        // Limit Specific Replacements (Evaluations: Any) (Current Round)
                        case "%p_x_th%":
                            value = limit.Activations(player.Name);
                            dict[key] = value.ToString() + Ordinal(value);
                            break;
                        case "%p_x%":
                            dict[key] = limit.Activations(player.Name).ToString();
                            break;
                        case "%s_x_th%":
                            value = limit.Activations(player.TeamId, player.SquadId);
                            dict[key] = value.ToString() + Ordinal(value);
                            break;
                        case "%s_x%":
                            dict[key] = limit.Activations(player.TeamId, player.SquadId).ToString();
                            break;
                        case "%t_x_th%":
                            value = limit.Activations(player.TeamId);
                            dict[key] = value.ToString() + Ordinal(value);
                            break;
                        case "%t_x%":
                            dict[key] = limit.Activations(player.TeamId).ToString();
                            break;
                        case "%a_x_th%":
                            value = limit.Activations();
                            dict[key] = value.ToString() + Ordinal(value);
                            break;
                        case "%a_x%":
                            dict[key] = limit.Activations().ToString();
                            break;

                        case "%r_x_th%":
                            value = limit.Spree(player.Name);
                            dict[key] = value.ToString() + Ordinal(value);
                            break;
                        case "%r_x%":
                            dict[key] = limit.Spree(player.Name).ToString();
                            break;


                        // Limit Specific Replacements (Evaluations: Any) (All Rounds)
                        case "%p_xa_th%":
                            value = limit.ActivationsTotal(player.Name);
                            dict[key] = value.ToString() + Ordinal(value);
                            break;
                        case "%p_xa%":
                            dict[key] = limit.ActivationsTotal(player.Name).ToString();
                            break;
                        case "%s_xa_th%":
                            value = limit.ActivationsTotal(player.TeamId, player.SquadId);
                            dict[key] = value.ToString() + Ordinal(value);
                            break;
                        case "%s_xa%":
                            dict[key] = limit.ActivationsTotal(player.TeamId, player.SquadId).ToString();
                            break;
                        case "%t_xa_th%":
                            value = limit.ActivationsTotal(player.TeamId);
                            dict[key] = value.ToString() + Ordinal(value);
                            break;
                        case "%t_xa%":
                            dict[key] = limit.ActivationsTotal(player.TeamId).ToString();
                            break;
                        case "%a_xa_th%":
                            value = limit.ActivationsTotal();
                            dict[key] = value.ToString() + Ordinal(value);
                            break;
                        case "%a_xa%":
                            dict[key] = limit.ActivationsTotal().ToString();
                            break;


                        // Other Replacements
                        case "%date%":
                            dict[key] = DateTime.Now.ToString("D");
                            break;
                        case "%time%":
                            dict[key] = DateTime.Now.ToString("t");
                            break;

                        case "%server_host%":
                            dict[key] = server_host;
                            break;
                        case "%server_port%":
                            dict[key] = server_port.ToString();
                            break;

                        case "%l_id%":
                            dict[key] = limit.id;
                            break;
                        case "%l_n%":
                            dict[key] = limit.Name;
                            break;

                        default:
                            dict[key] = key;
                            break;
                    }
                }
                catch (NullReferenceException)
                {
                    // this is expected for group events (g_e), so don't spam errors in the console
                    if (!((limit.Evaluation & g_e) > 0))
                        ConsoleWarn("could not determine replacement for %^b" + key.Replace("%", "") + "^n%");
                    dict[key] = key;
                }
            }


            //setup the advanced replacements


            Dictionary<String, Object> map = new Dictionary<String, Object>();
            map.Add("limit", (Object)limit);
            map.Add("player", (Object)player);
            map.Add("killer", (Object)killer);
            map.Add("kill", (Object)kill);
            map.Add("victim", (Object)victim);
            map.Add("plugin", (Object)this);
            map.Add("server", (Object)serverInfo);


            foreach (KeyValuePair<String, Object> pair in map)
            {
                String name = pair.Key;
                Object data = pair.Value;

                if (data == null)
                    continue;

                Type type = data.GetType();
                PropertyInfo[] props = type.GetProperties();

                foreach (PropertyInfo prop in props)
                {
                    String key = name + "." + prop.Name;

                    if (prop.Name.Equals("Item"))
                        continue;

                    //if (prop.PropertyType.Equals(typeof(bool)))
                    //    continue;

                    Object result = (Object)("?");

                    try
                    {
                        if (!AdvancedReplacementsDict.ContainsKey(key))
                            AdvancedReplacementsDict.Add(key, String.Empty);



                        result = prop.GetValue(data, null);

                        if (result == null)
                            continue;

                        if (result.GetType().Equals(typeof(Double)))
                            result = (Object)Math.Round((Double)result, 2);

                        AdvancedReplacementsDict[key] = result.ToString();
                    }
                    catch (Exception e)
                    {
                        DebugWrite("Advanced replacement failed for " + key + " with result " + result + " and error: " + e, 6);
                        ConsoleWarn("could not determine value for ^b" + key + "^n in replacement");
                        continue;
                    }
                }

            }







        }

        public String Ordinal(Double value)
        {

            Int64 last_XX = ((Int64)Math.Abs(value)) % 100;

            if ((last_XX > 10 && last_XX < 14) || last_XX == 0)
                return "th";

            Int64 last_X = last_XX % 10;

            switch (last_X)
            {
                case 1:
                    return "st";
                case 2:
                    return "nd";
                case 3:
                    return "rd";
                default:
                    return "th";
            }
        }

        public Boolean evaluateLimit(Limit limit, PlayerInfo killer, KillInfo kill, PlayerInfo victim)
        {
            return executeLimitAction(
                                           limit,
                                           null,
                                           killer,
                                           victim,
                                           kill
                                      );
        }

        // used for suicide events
        public Boolean evaluateLimit(Limit limit, PlayerInfo player, KillInfo kill)
        {
            return executeLimitAction(
                                            limit,
                                            player,
                                            null,
                                            null,
                                            kill
                                       );
        }

        //used for interval, join, team change, and spawn events
        public Boolean evaluateLimit(Limit limit, PlayerInfo player)
        {
            return executeLimitAction(
                                            limit,
                                            player,
                                            null,
                                            null,
                                            null
                                       );
        }

        //used for OnIntervalServer, RoundOver, and RoundStart events
        public Boolean evaluateLimit(Limit limit)
        {
            return executeLimitAction(
                                            limit,
                                            null,
                                            null,
                                            null,
                                            null
                                       );
        }


        public PlayerInfoInterface determineActionTarget(Limit limit, PlayerInfoInterface player, PlayerInfoInterface killer, PlayerInfoInterface victim)
        {
            switch (limit.Evaluation)
            {
                case Limit.EvaluationType.OnKill:
                case Limit.EvaluationType.OnTeamKill:
                    return killer;
                case Limit.EvaluationType.OnDeath:
                case Limit.EvaluationType.OnTeamDeath:
                    return victim;
                case Limit.EvaluationType.OnSuicide:
                    // for suicide, player, killer, and victim are the same
                    return player;
                case Limit.EvaluationType.OnSpawn:
                case Limit.EvaluationType.OnJoin:
                case Limit.EvaluationType.OnLeave:
                case Limit.EvaluationType.OnIntervalPlayers:
                case Limit.EvaluationType.OnAnyChat:
                case Limit.EvaluationType.OnTeamChange:
                    /*
                    case Limit.EvaluationType.OnGlobalChat:
                    case Limit.EvaluationType.OnTeamChat:
                    case Limit.EvaluationType.OnSquadChat:
                     */
                    return player;
                case Limit.EvaluationType.OnRoundOver:
                case Limit.EvaluationType.OnRoundStart:
                case Limit.EvaluationType.OnIntervalServer:
                    return DummyPlayer();
                default:
                    return null;
            }
        }

        PlayerInfo dummy = null;
        public PlayerInfoInterface DummyPlayer()
        {
            if (dummy != null)
                return dummy;

            CPunkbusterInfo pinfo = new CPunkbusterInfo("", "Unknown", "", "", "", "");
            CPlayerInfo info = new CPlayerInfo(pinfo.SoldierName, "", 0, 0);
            dummy = new PlayerInfo(this, pinfo);
            dummy.updateInfo(info);

            return dummy;

        }

        public Object[] buildLimitArguments(MethodInfo method, Limit limit, PlayerInfoInterface player, PlayerInfoInterface killer, PlayerInfoInterface victim, KillInfoInterface kill,
                                            TeamInfoInterface team1, TeamInfoInterface team2, TeamInfoInterface team3, TeamInfoInterface team4)
        {
            PluginInterface plugin = (PluginInterface)this;
            ServerInfoInterface server = (ServerInfoInterface)serverInfo;

            List<Object> arguments = null;
            switch (limit.Evaluation)
            {
                case Limit.EvaluationType.OnKill:
                case Limit.EvaluationType.OnDeath:
                case Limit.EvaluationType.OnTeamKill:
                case Limit.EvaluationType.OnTeamDeath:
                    arguments = new List<Object>(new Object[] { player, killer, kill, victim, server, plugin, team1, team2, team3, team4 });
                    break;
                case Limit.EvaluationType.OnSuicide:
                    // special case for suicide all three player, kill, and victim same
                    arguments = new List<Object>(new Object[] { player, player, kill, player, server, plugin, team1, team2, team3, team4 });
                    break;
                case Limit.EvaluationType.OnSpawn:
                case Limit.EvaluationType.OnJoin:
                case Limit.EvaluationType.OnLeave:
                case Limit.EvaluationType.OnIntervalPlayers:
                case Limit.EvaluationType.OnAnyChat:
                case Limit.EvaluationType.OnTeamChange:
                    /*
                    case Limit.EvaluationType.OnGlobalChat:
                    case Limit.EvaluationType.OnTeamChat:
                    case Limit.EvaluationType.OnSquadChat:
                     */
                    arguments = new List<Object>(new Object[] { player, server, plugin, team1, team2, team3, team4 });
                    break;
                case Limit.EvaluationType.OnRoundOver:
                case Limit.EvaluationType.OnRoundStart:
                case Limit.EvaluationType.OnIntervalServer:
                    arguments = new List<Object>(new Object[] { server, plugin, team1, team2, team3, team4 });
                    break;
                default:
                    throw new EvaluateException(FormatMessage("cannot determine arguments for " + limit.Evaluation.ToString() + " event in " + limit.ShortDisplayName, MessageType.Error));
            }

            if (method.Name.Equals("SecondCheck"))
                arguments.Add((LimitInfoInterface)limit);

            return arguments.ToArray();
        }

        public String buildFunctionArguments(Limit limit, String search, String class_source)
        {


            String extra = String.Empty;

            if (search.StartsWith("second"))
                extra += ", LimitInfoInterface limit";


            switch (limit.Evaluation)
            {
                case Limit.EvaluationType.OnKill:
                case Limit.EvaluationType.OnDeath:
                case Limit.EvaluationType.OnTeamKill:
                case Limit.EvaluationType.OnTeamDeath:
                    // for kill-death events, player is always the target of the Event action, e.g. Kill/killer, Death/dead,
                    return Regex.Replace(class_source, "%" + search + "%", "PlayerInfoInterface player, PlayerInfoInterface killer, KillInfoInterface kill, PlayerInfoInterface victim, ServerInfoInterface server, PluginInterface plugin, TeamInfoInterface team1, TeamInfoInterface team2, TeamInfoInterface team3, TeamInfoInterface team4" + extra);
                case Limit.EvaluationType.OnSuicide:
                    // special case for suicide all three player, kill, and victim same
                    return Regex.Replace(class_source, "%" + search + "%", "PlayerInfoInterface player, PlayerInfoInterface killer, KillInfoInterface kill, PlayerInfoInterface victim, ServerInfoInterface server, PluginInterface plugin, TeamInfoInterface team1, TeamInfoInterface team2, TeamInfoInterface team3, TeamInfoInterface team4" + extra);
                case Limit.EvaluationType.OnSpawn:
                case Limit.EvaluationType.OnJoin:
                case Limit.EvaluationType.OnLeave:
                case Limit.EvaluationType.OnIntervalPlayers:
                case Limit.EvaluationType.OnAnyChat:
                case Limit.EvaluationType.OnTeamChange:
                    /*
                      case Limit.EvaluationType.OnGlobalChat:
                      case Limit.EvaluationType.OnTeamChat:
                      case Limit.EvaluationType.OnSquadChat:
                     */
                    return Regex.Replace(class_source, "%" + search + "%", "PlayerInfoInterface player, ServerInfoInterface server, PluginInterface plugin, TeamInfoInterface team1, TeamInfoInterface team2, TeamInfoInterface team3, TeamInfoInterface team4" + extra);
                case Limit.EvaluationType.OnRoundOver:
                case Limit.EvaluationType.OnRoundStart:
                case Limit.EvaluationType.OnIntervalServer:
                    return Regex.Replace(class_source, "%" + search + "%", "ServerInfoInterface server, PluginInterface plugin, TeamInfoInterface team1, TeamInfoInterface team2, TeamInfoInterface team3, TeamInfoInterface team4" + extra);
                default:
                    throw new CompileException(FormatMessage("cannot determine arguments for ^b" + limit.Evaluation.ToString() + "^n event in " + limit.ShortDisplayName, MessageType.Error));
            }

        }



        /*
        // Not needed anymore, OnJoin limits are evaluated once only in OnPlayerJoin
        public Boolean shouldSkipEvaluation(Limit limit, PlayerInfoInterface player)
        {
            if (limit.Evaluation.Equals(Limit.EvaluationType.OnJoin))
                return limit.EvaluationsPlayer(player) > 0;

            return false;
        }*/


        public Boolean executeLimitCheck(Limit limit, String method, PlayerInfoInterface player, PlayerInfoInterface killer, PlayerInfoInterface victim, KillInfoInterface kill)
        {

            ServerInfoInterface server = this.serverInfo;

            Type class_type = limit.type;
            Object class_object = limit.evaluator;

            if (class_type == null || class_object == null)
                return false;

            MethodInfo class_method = null;
            // find the method through reflection
            if ((class_method = class_type.GetMethod(method)) == null)
                throw new EvaluateException(FormatMessage("could not find method ^b" + method + "^n, in " + limit.ShortDisplayName, MessageType.Error));


            Dictionary<Int32, TeamInfoInterface> teams = new Dictionary<Int32, TeamInfoInterface>();
            for (Int32 i = 1; i <= 4; i++)
            {
                if (!teams.ContainsKey(i))
                    teams.Add(i, (TeamInfoInterface)new TeamInfo(this, i, players, (ServerInfo)server));
            }

            // build the arguments
            Object[] arguments = buildLimitArguments(class_method, limit, player, killer, victim, kill, teams[1], teams[2], teams[3], teams[4]);

            // invoke the method
            Object result = class_method.Invoke(class_object, arguments);

            if (result == null)
                return false;

            return (Boolean)result;

        }






        //wrapper, to synchronize limit evaluation
        public Boolean executeLimitAction(
                                        Limit limit,
                                        PlayerInfoInterface player,
                                        PlayerInfoInterface killer,
                                        PlayerInfoInterface victim,
                                        KillInfoInterface kill
                                       )
        {

            lock (evaluation_mutex)
            {
                if (VModeSlot == null)
                    VModeSlot = Thread.AllocateDataSlot();

                Thread.SetData(VModeSlot, (Boolean)limit.Virtual);
                Boolean result = evaluateLimitChecks(limit, player, killer, victim, kill);
                Thread.SetData(VModeSlot, (Boolean)false);
                return result;
            }

        }

        static LocalDataStoreSlot VModeSlot = null;

        public Boolean evaluateLimitChecks(
                                        Limit limit,
                                        PlayerInfoInterface player,
                                        PlayerInfoInterface killer,
                                        PlayerInfoInterface victim,
                                        KillInfoInterface kill
                                       )
        {


            try
            {

                PlayerInfoInterface target = null;
                if ((target = determineActionTarget(limit, player, killer, victim)) == null)
                    throw new EvaluateException(FormatMessage("could not determine the ^itarget^n for ^baction^n with ^b" + limit.Evaluation.ToString() + "^n event in " + limit.ShortDisplayName, MessageType.Error));

                /*
                // Not needed anymore, OnJoin limits are evaluated once only in OnPlayerJoin
                // check wether we should evaluate this limit or not
                if (shouldSkipEvaluation(limit, target))
                    return false;
                */

                // quit now if first check is not enabled
                if (limit.FirstCheck.Equals(Limit.LimitType.Disabled))
                    return false;


                // call setup replacements early, in case user has a Code type of limit
                SetupReplacements(limit, target, killer, kill, victim);


                Boolean result = executeLimitCheck(limit, "FirstCheck", target, killer, victim, kill);



                /*
                // Not needed anymore, OnJoin limits are evaluated once only in OnPlayerJoin
                // do some record keeping
                if (limit.Evaluation.Equals(Limit.EvaluationType.OnJoin))
                    limit.RecordEvaluation(target);
                */

                if (!result)
                    return false;

                // more book-keeping
                limit.RecordActivation(target.Name);
                limit.RecordSpree(target.Name);

                //this is the actual call for setup up replacements that matter
                SetupReplacements(limit, target, killer, kill, victim);

                // run the second phase if available
                if (!limit.SecondCheck.Equals(Limit.LimitType.Disabled) && !limit.SecondCheckEmpty)
                    result = executeLimitCheck(limit, "SecondCheck", target, killer, victim, kill);


                if (!result)
                    return false;



                Limit.LimitAction action = limit.Action;

                if (action.Equals(Limit.LimitAction.None))
                {
                    DebugWrite("^b" + target.Name + "^n activated " + limit.ShortDisplayName, 3);
                    return true;
                }



                if ((action & Limit.LimitAction.Say) > 0)
                {
                    action = action & ~Limit.LimitAction.Say;

                    Int32 delay = limit.SayDelay;

                    String message = R(limit.SayMessage);
                    DebugWrite("say(" + limit.SayAudience.ToString() + "), ^b" + target.Name + "^n, activated " + limit.ShortDisplayName + ": " + message, 3);

                    Boolean chat = limit.SayProConChat;
                    MessageAudience audience = limit.SayAudience;

                    switch (audience)
                    {
                        case MessageAudience.All:
                            SendGlobalMessage(message, delay);
                            if (chat)
                                PRoConChat("Admin > All: " + message);
                            break;
                        case MessageAudience.Team:
                            SendTeamMessage(target.TeamId, message, delay);
                            if (chat)
                                PRoConChat("Admin > Team(" + target.TeamId + "): " + message);
                            break;
                        case MessageAudience.Squad:
                            SendSquadMessage(target.TeamId, target.SquadId, message, delay);
                            if (chat)
                                PRoConChat("Admin > Team(" + target.TeamId + ").Squad(" + target.SquadId + "): " + message);
                            break;
                        case MessageAudience.Player:
                            SendPlayerMessage(target.Name, message, delay);
                            if (chat)
                                PRoConChat("Admin > " + player.Name + ": " + message);
                            break;
                        default:
                            ConsoleError("Unknown " + typeof(MessageAudience).Name + " for " + limit.ShortDisplayName);
                            break;
                    }



                    // exit early if action is only say
                    if (action.Equals(Limit.LimitAction.Say))
                        return !VMode;
                }


                if ((action & Limit.LimitAction.Yell) > 0)
                {
                    action = action & ~Limit.LimitAction.Yell;

                    Int32 duration = limit.YellDuration;

                    String message = R(limit.YellMessage);
                    DebugWrite("yell(" + limit.YellAudience.ToString() + "), ^b" + target.Name + "^n, activated " + limit.ShortDisplayName + ": " + message, 3);

                    Boolean chat = limit.YellProConChat;
                    MessageAudience audience = limit.YellAudience;

                    switch (audience)
                    {
                        case MessageAudience.All:
                            SendGlobalYell(message, duration);
                            if (chat)
                                PRoConChat("Admin > Yell All: " + message);
                            break;
                        case MessageAudience.Team:
                            SendTeamYell(target.TeamId, message, duration);
                            if (chat)
                                PRoConChat("Admin > Yell Team(" + target.TeamId + "): " + message);
                            break;
                        case MessageAudience.Player:
                            SendPlayerYell(target.Name, message, duration);
                            if (chat)
                                PRoConChat("Admin > Yell " + player.Name + ": " + message);
                            break;
                        case MessageAudience.Squad:
                        /*
                        SendSquadYell(target.TeamId, target.SquadId, message, duration);
                        if (chat)
                            PRoConChat("Admin > Team(" + target.TeamId + ").Squad(" + target.SquadId + "): " + message);
                        */
                        default:
                            ConsoleError("Unknown " + typeof(MessageAudience).Name + " for " + limit.ShortDisplayName);
                            break;
                    }



                    // exit early if action is only yell
                    if (action.Equals(Limit.LimitAction.Yell))
                        return !VMode;
                }

                if ((action & Limit.LimitAction.Log) > 0)
                {
                    action = action & ~Limit.LimitAction.Log;

                    String lmessage = R(limit.LogMessage);

                    Limit.LimitLogDestination destination = limit.LogDestination;

                    if ((destination & Limit.LimitLogDestination.Plugin) > 0)
                        ConsoleWrite(lmessage);

                    if ((destination & Limit.LimitLogDestination.File) > 0)
                        Log(limit.LogFile, lmessage);

                    // exit early if action is only log
                    if (action.Equals(Limit.LimitAction.Log))
                        return !VMode;
                }

                if ((action & Limit.LimitAction.Mail) > 0)
                {
                    action = action & ~Limit.LimitAction.Mail;

                    String message = R(limit.MailBody);
                    String subject = R(limit.MailSubject);
                    String address = limit.MailAddress;


                    DebugWrite("sending mail(" + address + ") player ^b" + target.Name + "^n, (activated " + limit.ShortDisplayName + "), with subject: \"" + subject + "\"", 3);
                    SendMail(address, subject, message);

                    // exit early if action is only log
                    if (action.Equals(Limit.LimitAction.Mail))
                        return !VMode;
                }

                if ((action & Limit.LimitAction.SMS) > 0)
                {
                    action = action & ~Limit.LimitAction.SMS;

                    String number = limit.SMSNumber;
                    String message = R(limit.SMSMessage);
                    String country = limit.SMSCountry;
                    String carrier = limit.SMSCarrier;


                    DebugWrite("sending SMS(" + number + ") player ^b" + target.Name + "^n, (activated " + limit.ShortDisplayName + ")", 3);
                    SendSMS(country, carrier, number, message);

                    // exit early if action is only SMS
                    if (action.Equals(Limit.LimitAction.SMS))
                        return !VMode;
                }

                if ((action & Limit.LimitAction.Tweet) > 0)
                {
                    action = action & ~Limit.LimitAction.Tweet;

                    String status = R(limit.TweetStatus);
                    String account = getStringVarValue("twitter_screen_name");


                    DebugWrite("sending Tweet (@" + account + "): \"" + status + "\"", 3);
                    Tweet(status);

                    // exit early if action is only Tweet
                    if (action.Equals(Limit.LimitAction.Tweet))
                        return !VMode;
                }

                if ((action & Limit.LimitAction.PRoConChat) > 0)
                {
                    action = action & ~Limit.LimitAction.PRoConChat;

                    String text = R(limit.PRoConChatText);


                    DebugWrite("sending procon-chat \"" + text + "\"", 3);
                    PRoConChat(text);

                    // exit early if action is only PRoConChat
                    if (action.Equals(Limit.LimitAction.PRoConChat))
                        return !VMode;
                }


                if ((action & Limit.LimitAction.PRoConEvent) > 0)
                {
                    action = action & ~Limit.LimitAction.PRoConEvent;


                    EventType type = limit.PRoConEventType;
                    CapturableEvent name = limit.PRoConEventName;
                    String text = R(limit.PRoConEventText);
                    String pname = R(limit.PRoConEventPlayer);


                    DebugWrite("sending procon event(type:^b" + type.ToString() + "^n, name: ^b" + name.ToString() + "^n, player:^b" + pname + "^n) \"" + text + "\"", 3);

                    PRoConEvent(type, name, text, pname);

                    // exit early if action is only PRoConEvent
                    if (action.Equals(Limit.LimitAction.PRoConEvent))
                        return !VMode;
                }


                if ((action & Limit.LimitAction.TaskbarNotify) > 0)
                {
                    action = action & ~Limit.LimitAction.TaskbarNotify;

                    DebugWrite("sending taskbar notification,  player ^b" + target.Name + "^n, (activated " + limit.ShortDisplayName + ")", 3);
                    SendTaskbarNotification(R(limit.TaskbarNotifyTitle), R(limit.TaskbarNotifyMessage));

                    // exit early if action is only TaskbarNotify
                    if (action.Equals(Limit.LimitAction.TaskbarNotify))
                        return !VMode;
                }

                if ((action & Limit.LimitAction.SoundNotify) > 0)
                {
                    action = action & ~Limit.LimitAction.SoundNotify;

                    DebugWrite("playing soundnotification,  player ^b" + target.Name + "^n, (activated " + limit.ShortDisplayName + ")", 3);
                    SendSoundNotification(R(limit.SoundNotifyFile), R(limit.SoundNotifyRepeat));

                    // exit early if action is only TaskbarNotify
                    if (action.Equals(Limit.LimitAction.SoundNotify))
                        return !VMode;
                }

                /* Actions that possibly affect server state */

                result = false;

                if ((action & Limit.LimitAction.EABan) > 0)
                {
                    action = action & ~Limit.LimitAction.EABan;

                    EABanType btype = limit.EABType;
                    EABanDuration bduration = limit.EABDuration;

                    String bmessage = R(limit.EABMessage);
                    Int32 bminutes = limit.EABMinutes;

                    DebugWrite("ea-banning(" + btype.ToString() + ":" + bduration.ToString() + ") player ^b" + target.Name + "^n, (activated " + limit.ShortDisplayName + "), with message: \"" + bmessage + "\"", 1);
                    if (EABanPlayerWithMessage(btype, bduration, target.Name, bminutes, bmessage))
                        result = true;
                }




                if ((action & Limit.LimitAction.PBBan) > 0)
                {
                    action = action & ~Limit.LimitAction.PBBan;

                    PBBanDuration bduration = limit.PBBDuration;

                    String bmessage = R(limit.PBBMessage);
                    Int32 bminutes = limit.PBBMinutes;

                    DebugWrite("pb-banning(" + bduration.ToString() + ") player ^b" + target.Name + "^n, (activated " + limit.ShortDisplayName + "), with message: \"" + bmessage + "\"", 1);

                    if (PBBanPlayerWithMessage(bduration, target.Name, bminutes, bmessage))
                        result = true;
                }

                if ((action & Limit.LimitAction.PBCommand) > 0)
                {
                    action = action & ~Limit.LimitAction.PBCommand;

                    String command_text = R(limit.PBCommandText);

                    DebugWrite("sending pb-command (^b" + target.Name + "^n, activated " + limit.ShortDisplayName + "): " + command_text, 3);

                    if (PBCommand(command_text))
                        result = true;
                }

                if ((action & Limit.LimitAction.ServerCommand) > 0)
                {
                    action = action & ~Limit.LimitAction.ServerCommand;

                    String command_text = limit.ServerCommandText;

                    DebugWrite("sending server-command (^b" + target.Name + "^n, activated " + limit.ShortDisplayName + "): " + command_text, 3);

                    if (SCommand(command_text))
                        result = true;
                }

                if ((action & Limit.LimitAction.Kick) > 0)
                {
                    action = action & ~Limit.LimitAction.Kick;

                    String kmessage = R(limit.KickMessage);
                    DebugWrite("kicking player ^b" + target.Name + "^n, (activated " + limit.ShortDisplayName + "), with message: \"" + kmessage + "\"", 1);

                    if (KickPlayerWithMessage(target.Name, kmessage))
                        result = true;
                }

                if ((action & Limit.LimitAction.Kill) > 0)
                {
                    action = action & ~Limit.LimitAction.Kill;

                    Int32 delay = limit.KillDelay;

                    String delay_text = "";
                    if (delay > 0)
                        delay_text = "(delay: ^b" + delay + "^n)";

                    DebugWrite("killing" + delay_text + " player ^b" + target.Name + "^n, (activated " + limit.ShortDisplayName + ")", 2);

                    if (KillPlayer(target.Name, delay))
                        result = true;
                }



                if ((action & (Limit.LimitAction)0xFF) > 0)
                    throw new EvaluateException(FormatMessage("unknown limit action " + action.ToString() + " for " + limit.ShortDisplayName, MessageType.Error));

                return result;

            }
            catch (EvaluateException e)
            {
                LogWrite(e.Message);
                return false;
            }
            catch (Exception e)
            {
                if (limit == null)
                    DumpException(e);
                else
                    DumpException(e, limit.ShortName);
            }

            return true;
        }
    }
}
