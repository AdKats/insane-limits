using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Flurl.Http;

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
        public class CustomList
        {
            public enum ListState
            {
                Enabled = 0x01,
                Disabled = 0x02
            };

            public enum ListComparison
            {
                CaseSensitive = 0x01,
                CaseInsensitve = 0x02
            };

            public static List<String> valid_fields = new List<String>(new String[]
                {
                "id", "hide", "state", "name", "comparison", "data", "delete"
                });

            public Dictionary<String, String> fields = null;
            public InsaneLimits plugin = null;

            public String Name
            {
                get { return fields["name"]; }
            }

            public String FullDisplayName
            {
                get { return "List #^b" + id + "^n - " + Name; }
            }

            public String FullName
            {
                get { return "List #" + id + " - " + Name; }
            }

            public String ShortName
            {
                get { return "List #" + id; }
            }

            public String ShortDisplayName
            {
                get { return "List #" + id; }
            }

            public String id
            {
                get { return fields["id"]; }
            }

            public ListState State
            {
                get { return (ListState)Enum.Parse(typeof(ListState), fields["state"]); }
            }

            public ListComparison Comparison
            {
                get { return (ListComparison)Enum.Parse(typeof(ListComparison), fields["comparison"]); }
            }

            public ShowHide Hide
            {
                get { return ((ShowHide)Enum.Parse(typeof(ShowHide), fields["hide"])); }
            }

            public String Data
            {
                get { return fields["data"]; }
            }

            public Boolean Contains(String item)
            {
                List<String> items = new List<String>(Regex.Split(Data, @"\s*,\s*"));
                Boolean ci = Comparison.Equals(ListComparison.CaseInsensitve);
                String ritem = items.Find(delegate (String citem) { return String.Compare(citem, item, ci) == 0; });

                return ritem != null;
            }

            private void SetupFields()
            {
                fields = new Dictionary<String, String>();
                foreach (String field_key in valid_fields)
                    fields.Add(field_key, "");
            }

            private void InitFields(String id)
            {
                String auto_hide = (plugin.getBooleanVarValue("auto_hide_sections") ? ShowHide.Hide : ShowHide.Show).ToString();

                setFieldValue("id", id);
                setFieldValue("hide", auto_hide);
                setFieldValue("state", ListState.Enabled.ToString());
                setFieldValue("name", "Name" + id);
                setFieldValue("comparison", ListComparison.CaseInsensitve.ToString());
                setFieldValue("data", "value1, value2, value3");
                setFieldValue("delete", false.ToString());

            }

            public static String extractFieldKey(String var)
            {
                Match match = Regex.Match(var, @"list_[^_]+_([^0-9]+)");
                if (match.Success)
                    return match.Groups[1].Value;

                return var;
            }

            public Boolean isValidFieldKey(String var)
            {

                if (valid_fields.Contains(extractFieldKey(var)))
                    return true;

                return false;
            }

            public static String extractId(String var)
            {
                Match vmatch = Regex.Match(var, @"^list_([^_]+)");
                if (vmatch.Success)
                    return vmatch.Groups[1].Value;

                return "UnknownId";
            }

            public String getField(String var)
            {
                if (!isValidFieldKey(var))
                    return "";

                String field_key = extractFieldKey(var);
                return fields[field_key];
            }

            public Boolean setFieldValue(String var, String val)
            {
                return setFieldValue(var, val, false);
            }

            public Boolean setFieldValue(String var, String val, Boolean ui)
            {
                //plugin.ConsoleWrite("Setting: " +var +" = " + val);
                String field_key = extractFieldKey(var);
                if (!isValidFieldKey(field_key))
                    return false;

                return validateAndSetFieldValue(field_key, val, ui);

            }

            public Boolean validateAndSetFieldValue(String field, String val, Boolean ui)
            {   //plugin.ConsoleWrite(field + " = " + val + ", UI: " + ui.ToString());
                if (field.Equals("delete"))
                {
                    /* Parse Boolean Values */
                    Boolean booleanValue = false;

                    if (Regex.Match(val, @"^\s*(1|true|yes)\s*$", RegexOptions.IgnoreCase).Success)
                        booleanValue = true;
                    else if (Regex.Match(val, @"^\s*(0|false|no)\s*$", RegexOptions.IgnoreCase).Success)
                        booleanValue = false;
                    else
                        return false;

                    fields[field] = booleanValue.ToString();
                }
                else if (field.Equals("state") ||
                         field.Equals("hide") ||
                         field.Equals("comparison")
                    )
                {
                    /* Parse Enum */
                    Type type = null;
                    if (field.Equals("state"))
                        type = typeof(ListState);
                    else if (field.Equals("hide"))
                        type = typeof(ShowHide);
                    else if (field.Equals("comparison"))
                        type = typeof(ListComparison);

                    try
                    {
                        fields[field] = Enum.Format(type, Enum.Parse(type, val, true), "G").ToString();

                        return true;
                    }
                    catch (FormatException)
                    {
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return false;
                    }

                }
                else if (field.Equals("data"))
                {
                    List<String> items = new List<String>(Regex.Split(val, @"\s*,\s*"));
                    items.RemoveAll(delegate (String item) { return item == null || item.Trim().Length == 0; });
                    fields[field] = String.Join(", ", items.ToArray());
                }
                else
                    fields[field] = val;

                return true;
            }

            public static Boolean isListVar(String var)
            {

                if (Regex.Match(var, @"^list_[^_]+_(" + String.Join("|", valid_fields.ToArray()) + ")").Success)
                    return true;

                return false;
            }

            public Dictionary<String, String> getSettings(Boolean display)
            {

                Dictionary<String, String> settings = new Dictionary<String, String>();

                /* optimization */
                if (display && Hide.Equals(ShowHide.Hide))
                {
                    settings.Add("list_" + id + "_hide", Hide.ToString());
                    return settings;
                }

                List<String> keys = new List<String>(fields.Keys);
                for (Int32 i = 0; i < keys.Count; i++)
                {
                    String key = keys[i];
                    if (!fields.ContainsKey(key))
                        continue;

                    String value = fields[key];
                    settings.Add("list_" + id + "_" + key, value);

                }

                return settings;
            }

            public Boolean shouldSkipFieldKey(String name)
            {
                try
                {
                    if (!plugin.Agreement)
                        return true;

                    if (!isValidFieldKey(name))
                        return false;

                    String field_key = extractFieldKey(name);

                    if (Hide.Equals(ShowHide.Hide) && !field_key.Equals("hide"))
                        return true;

                    if (Regex.Match(field_key, @"(id|delete)$").Success)
                        return true;
                }
                catch (Exception e)
                {
                    plugin.DumpException(e);
                }
                return false;

            }

            public CustomList(InsaneLimits plugin, String id)
            {
                this.plugin = plugin;
                SetupFields();
                InitFields(id);
            }
        }

        public class LimitEvent
        {
            public readonly DateTime Time;
            public String Target;
            public Int32 TeamId;
            public Int32 SquadId;

            String MapFile;
            String Gamemode;
            Limit.LimitAction Action;
            Limit.EvaluationType Evaluation;

            public LimitEvent(Limit limit, PlayerInfoInterface player, ServerInfoInterface server)
            {
                Time = DateTime.Now;
                Target = player.Name;
                TeamId = player.TeamId;
                SquadId = player.SquadId;

                MapFile = server.MapFileName;
                Gamemode = server.Gamemode;
                Action = limit.Action;
                Evaluation = limit.Evaluation;
            }
        }

        public class Limit : LimitInfoInterface
        {
            public InsaneLimits plugin;

            public enum EvaluationType
            {
                OnInterval = 0x0001,
                OnIntervalPlayers = 0x0001,   // duplicate of OnInterval
                OnKill = 0x0002,
                OnDeath = 0x0004,
                OnTeamKill = 0x0008,
                OnTeamDeath = 0x0010,
                OnSuicide = 0x0020,
                OnSpawn = 0x0040,
                OnJoin = 0x0080,
                OnAnyChat = 0x0100,
                OnRoundOver = 0x0200,
                OnRoundStart = 0x0400,
                OnTeamChange = 0x0800,
                OnIntervalServer = 0x1000,
                OnLeave = 0x2000
                /*
                OnGlobalChat = 0x200,
                OnSquadChat  = 0x400,
                OnTeamChat   = 0x800
                */
            };

            public enum LimitType
            {
                Disabled = 0x01,
                Expression = 0x02,
                Code = 0x04
            };

            public enum LimitAction
            {
                None = Actions.None,
                Kick = Actions.Kick,
                Kill = Actions.Kill,
                PBBan = Actions.PBBan,
                EABan = Actions.EABan,
                Say = Actions.Say,
                SMS = Actions.SMS,
                Mail = Actions.Mail,
                Log = Actions.Log,
                TaskbarNotify = Actions.TaskbarNotify,
                Tweet = Actions.Tweet,
                PBCommand = Actions.PBCommand,
                ServerCommand = Actions.ServerCommand,
                PRoConEvent = Actions.PRoConEvent,
                PRoConChat = Actions.PRoConChat,
                SoundNotify = Actions.SoundNotify,
                Yell = Actions.Yell
            };

            public enum LimitState
            {
                Enabled = 0x01,
                Disabled = 0x02,
                Virtual = 0x04,
            };

            public enum LimitScope
            {
                Players = 0x01,
                Server = 0x02
            };

            public enum LimitLogDestination
            {
                File = 0x01,
                Plugin = 0x02,
                Both = 0x01 | 0x02
            };

            public Int64 IntervalCount = Int64.MaxValue;
            public DateTime LastInterval = new DateTime(1970, 1, 1);
            public Object evaluator = null;
            public Type type = null;

            public Dictionary<String, List<LimitEvent>> activations = null;
            /*
            // Not needed anymore, OnJoin limits are evaluated once only in OnPlayerJoin
            public Dictionary<String, List<LimitEvent>> evaluations = null;
             */
            public Dictionary<String, List<LimitEvent>> activations_total = null;
            public Dictionary<String, Double> sprees = null;

            public Dictionary<String, String> fields;

            public Dictionary<String, String> group2title;
            public Dictionary<String, String> group2regex;
            public Dictionary<String, String> title2group;

            public DataDictionary DataDict;
            public DataDictionary RoundDataDict;

            public static String[] valid_groups = new String[]
            {
                "Kick Action", "kick_group", @"^kick_",
                "Kill Action", "kill_group", @"^kill_",
                "Say Action",  "say_group", @"^say_",
                "EABan Action", "ea_ban_group", @"^ea_ban_",
                "PBBan Action", "pb_ban_group", @"^pb_ban_",
                "PBCommand Action", "pb_command_group", @"^pb_command_",
                "PRoConEvent Action", "procon_event_group", @"^procon_event_",
                "PRoConChat Action", "procon_chat_group", @"^procon_chat_",
                "ServerCommand Action", "server_command_group", @"^server_command_",
                "Taskbar Notify Action", "taskbar_notify_group", @"^taskbar_",
                "Log Action", "log_group", @"^log_",
                "SMS Action", "sms_group", @"^sms_",
                "Mail Action", "mail_group", @"^mail_",
                "Tweet Action", "tweet_group", @"^tweet_",
                "Sound Notify Action", "sound_notify_group", @"^sound_",
                "Yell Action",  "yell_group", @"^yell_",
            };

            public static List<String> valid_fields = new List<String>(new String[] {
                "id", "hide", "state", "name",
                "evaluation", "evaluation_interval",
                "first_check", "first_check_expression", "first_check_code",
                "second_check", "second_check_code", "second_check_expression",
                "new_action", "action",
                "kick_group", "kick_message",
                "kill_group", "kill_delay",
                "say_group", "say_message", "say_audience", "say_delay", "say_procon_chat",
                "ea_ban_group", "ea_ban_type", "ea_ban_duration", "ea_ban_minutes", "ea_ban_message",
                "pb_ban_group", "pb_ban_type", "pb_ban_duration", "pb_ban_minutes", "pb_ban_message",
                "pb_command_group", "pb_command_text",
                "procon_chat_group", "procon_chat_text",
                "procon_event_group", "procon_event_type", "procon_event_name", "procon_event_text", "procon_event_player",
                "server_command_group", "server_command_text",
                "taskbar_notify_group", "taskbar_notify_title", "taskbar_notify_message",
                "log_group", "log_destination", "log_file", "log_message",
                "sms_group", "sms_country", "sms_carrier", "sms_number", "sms_message",
                "mail_group", "mail_address", "mail_subject", "mail_body",
                "tweet_group", "tweet_status",
                "sound_notify_group", "sound_notify_file", "sound_notify_repeat",
                "delete",
                "yell_group", "yell_message", "yell_audience", "yell_duration", "yell_procon_chat",
                });

            public DataDictionaryInterface Data { get { return (DataDictionaryInterface)DataDict; } }
            public DataDictionaryInterface RoundData { get { return (DataDictionaryInterface)RoundDataDict; } }
            public DataDictionaryInterface DataRound { get { return (DataDictionaryInterface)RoundDataDict; } }

            public String id
            {
                get { return fields["id"]; }
            }

            public String Name
            {
                get { return fields["name"]; }
            }

            public String FullDisplayName
            {
                get { return "Limit #^b" + id + "^n - " + Name; }
            }

            public String FullName
            {
                get { return "Limit #" + id + " - " + Name; }
            }

            public String FullReplaceName
            {
                get { return "Limit #%l_id% %l_n%"; }
            }

            public String ShortName
            {
                get { return "Limit #" + id; }
            }

            public String ShortDisplayName
            {
                get { return "Limit #^b" + id + "^n"; }
            }

            public ShowHide Hide
            {
                get { return ((ShowHide)Enum.Parse(typeof(ShowHide), fields["hide"])); }
            }

            public Boolean Enabled
            {
                get { return State.Equals(LimitState.Enabled); }
            }

            public Boolean Virtual
            {
                get { return State.Equals(LimitState.Virtual); }
            }

            public Boolean Disabled
            {
                get { return State.Equals(LimitState.Disabled); }
            }

            public EvaluationType Evaluation
            {
                get { return (EvaluationType)Enum.Parse(typeof(EvaluationType), fields["evaluation"]); }
            }

            public LimitState State
            {
                get { return (LimitState)Enum.Parse(typeof(LimitState), fields["state"]); }
            }

            public Int64 Interval
            {
                get { return Int64.Parse(fields["evaluation_interval"]); }
            }

            public Boolean FirstCheckEmpty
            {
                get
                {
                    return (FirstCheck.Equals(LimitType.Expression) && FirstCheckExpression.Length == 0) ||
                           (FirstCheck.Equals(LimitType.Code) && FirstCheckCode.Length == 0);
                }
            }

            public Boolean SecondCheckEmpty
            {
                get
                {
                    return (SecondCheck.Equals(LimitType.Expression) && SecondCheckEpression.Length == 0) ||
                           (SecondCheck.Equals(LimitType.Code) && SecondCheckCode.Length == 0);
                }
            }

            public LimitAction Action
            {
                get
                {
                    return Str2Action(fields["action"]);
                }
            }

            private List<String> CleanupActions(String actions)
            {
                List<String> alist = new List<String>(Regex.Split(actions, @"\s*\|\s*"));
                List<String> clean = new List<String>();
                String clean_action = String.Empty;

                foreach (String action in alist)
                {
                    try
                    {
                        LimitAction caction = Str2Action(action);

                        if (caction.Equals(LimitAction.None))
                            continue;

                        if (clean.Contains(caction.ToString()))
                            continue;

                        clean.Add(caction.ToString());
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                }

                if (clean.Count == 0)
                    clean.Add(LimitAction.None.ToString());

                return clean;
            }

            public List<String> ActionsList
            {
                get
                {
                    return CleanupActions(fields["action"]);
                }
                set
                {
                    fields["action"] = String.Join(" | ", value.ToArray());
                }
            }

            private LimitAction Str2Action(String action)
            {
                return (LimitAction)Enum.Parse(typeof(LimitAction), action.Replace("|", ","), true);
            }

            public MessageAudience SayAudience
            {
                get { return (MessageAudience)Enum.Parse(typeof(MessageAudience), fields["say_audience"]); }
            }

            public Boolean SayProConChat
            {
                get { return ((TrueFalse)Enum.Parse(typeof(TrueFalse), fields["say_procon_chat"])).Equals(TrueFalse.True); }
            }

            public MessageAudience YellAudience
            {
                get { return (MessageAudience)Enum.Parse(typeof(MessageAudience), fields["yell_audience"]); }
            }

            public Boolean YellProConChat
            {
                get { return ((TrueFalse)Enum.Parse(typeof(TrueFalse), fields["yell_procon_chat"])).Equals(TrueFalse.True); }
            }

            public LimitType SecondCheck
            {
                get { return (LimitType)Enum.Parse(typeof(LimitType), fields["second_check"]); }
            }

            public LimitType FirstCheck
            {
                get { return (LimitType)Enum.Parse(typeof(LimitType), fields["first_check"]); }
            }

            public EABanDuration EABDuration
            {
                get { return (EABanDuration)Enum.Parse(typeof(EABanDuration), fields["ea_ban_duration"]); }
            }

            public EABanType EABType
            {
                get { return (EABanType)Enum.Parse(typeof(EABanType), fields["ea_ban_type"]); }
            }

            public PBBanDuration PBBDuration
            {
                get { return (PBBanDuration)Enum.Parse(typeof(PBBanDuration), fields["pb_ban_duration"]); }
            }

            public PBBanType PBBType
            {
                get { return (PBBanType)Enum.Parse(typeof(PBBanType), fields["pb_ban_type"]); }
            }

            public String PBCommandText
            {
                get { return fields["pb_command_text"]; }
            }

            public EventType PRoConEventType
            {
                get { return (EventType)Enum.Parse(typeof(EventType), fields["procon_event_type"]); }
            }

            public CapturableEvent PRoConEventName
            {
                get { return (CapturableEvent)Enum.Parse(typeof(CapturableEvent), fields["procon_event_name"]); }
            }

            public String PRoConEventText
            {
                get { return fields["procon_event_text"]; }
            }

            public String PRoConEventPlayer
            {
                get { return fields["procon_event_player"]; }
            }

            public String PRoConChatText
            {
                get { return fields["procon_chat_text"]; }
            }

            public String ServerCommandText
            {
                get { return fields["server_command_text"]; }
            }

            public LimitLogDestination LogDestination
            {
                get { return (LimitLogDestination)Enum.Parse(typeof(LimitLogDestination), fields["log_destination"]); }
            }

            public String LogFile
            {
                get { return fields["log_file"]; }
            }

            public String LogMessage
            {
                get { return fields["log_message"]; }
            }

            public String MailAddress
            {
                get { return fields["mail_address"]; }
            }

            public String MailSubject
            {
                get { return fields["mail_subject"]; }
            }

            public String MailBody
            {
                get { return fields["mail_body"]; }
            }

            public String SMSCountry
            {
                get { return fields["sms_country"]; }
            }

            public String SMSCarrier
            {
                get { return fields["sms_carrier"]; }

                set { fields["sms_carrier"] = value; }
            }

            public String TweetStatus
            {
                get { return fields["tweet_status"]; }
            }

            public String SMSNumber
            {
                get { return fields["sms_number"]; }
            }

            public String SMSMessage
            {
                get { return fields["sms_message"]; }
            }

            public String FirstCheckCode
            {
                get { return fields["first_check_code"].Trim(); }
            }

            public String FirstCheckExpression
            {
                get { return fields["first_check_expression"].Trim(); }
            }

            public String SecondCheckCode
            {
                get { return fields["second_check_code"].Trim(); }
            }

            public String SecondCheckEpression
            {
                get { return fields["second_check_expression"].Trim(); }
            }

            public String SayMessage
            {
                get { return fields["say_message"]; }
            }

            public String YellMessage
            {
                get { return fields["yell_message"]; }
            }

            public String PBBMessage
            {
                get { return fields["pb_ban_message"]; }
            }

            public String EABMessage
            {
                get { return fields["ea_ban_message"]; }
            }

            public String KickMessage
            {
                get { return fields["kick_message"]; }
            }

            public Int32 SayDelay
            {
                get { return Int32.Parse(fields["say_delay"]); }
            }

            public Int32 YellDuration
            {
                get { return Int32.Parse(fields["yell_duration"]); }
            }

            public Int32 KillDelay
            {
                get { return Int32.Parse(fields["kill_delay"]); }
            }

            public Int32 PBBMinutes
            {
                get { return Int32.Parse(fields["pb_ban_minutes"]); }
            }

            public Int32 EABMinutes
            {
                get { return Int32.Parse(fields["ea_ban_minutes"]); }
            }

            public Boolean Valid
            {
                get { return !Invalid; }
            }

            public Boolean Invalid
            {
                get
                {
                    return Disabled ||
                            evaluator == null ||
                            FirstCheck.Equals(LimitType.Disabled) ||
                            FirstCheckEmpty;
                }
            }

            public String TaskbarNotifyMessage
            {
                get { return fields["taskbar_notify_message"]; }
            }

            public String TaskbarNotifyTitle
            {
                get { return fields["taskbar_notify_title"]; }
            }

            public String SoundNotifyFile
            {
                get { return fields["sound_notify_file"]; }
            }
            public String SoundNotifyRepeat
            {
                get { return fields["sound_notify_repeat"]; }
            }

            public void RecordActivation(String PlayerName)
            {
                if (plugin.serverInfo == null)
                    return;

                ServerInfo server = plugin.serverInfo;

                if (!plugin.players.ContainsKey(PlayerName) || plugin.players[PlayerName] == null)
                    return;

                PlayerInfo player = plugin.players[PlayerName];

                if (!activations.ContainsKey(player.Name))
                    activations.Add(player.Name, new List<LimitEvent>());

                activations[player.Name].Add(new LimitEvent(this, player, server));

            }

            /*
            // Not needed anymore, OnJoin limits are evaluated once only in OnPlayerJoin
            public void RecordEvaluation(PlayerInfoInterface player)
            {
                if (plugin.serverInfo == null)
                    return;

                ServerInfoInterface server = (ServerInfoInterface)plugin.serverInfo;

                if (!evaluations.ContainsKey(player.Name))
                    evaluations.Add(player.Name, new List<LimitEvent>());

                evaluations[player.Name].Add(new LimitEvent(this, player, server));
            }
             */

            public Double Activations()
            {
                Double total = 0;
                foreach (KeyValuePair<String, List<LimitEvent>> pair in activations)
                    if (pair.Value != null)
                        total += pair.Value.Count;

                return total;
            }

            public Double ActivationsTotal()
            {
                Double total = Activations();

                foreach (KeyValuePair<String, List<LimitEvent>> pair in activations_total)
                    if (pair.Value != null)
                        total += pair.Value.Count;

                return total;

            }

            public Double Activations(String PlayerName)
            {

                if (!activations.ContainsKey(PlayerName))
                    return 0;

                return activations[PlayerName].Count;
            }

            public Double ActivationsTotal(String PlayerName)
            {
                Double total = Activations(PlayerName);

                if (!activations_total.ContainsKey(PlayerName))
                    return total;

                return total + activations_total[PlayerName].Count;
            }

            public Double Activations(Int32 TeamId, Int32 SquadId)
            {
                Double total = 0;

                //we have to visit every possible limit activation and count
                foreach (KeyValuePair<String, List<LimitEvent>> pair in activations)
                    if (pair.Value != null)
                        foreach (LimitEvent e in pair.Value)
                            if (e.TeamId == TeamId && e.SquadId == SquadId)
                                total++;

                return total;
            }

            public Double ActivationsTotal(Int32 TeamId, Int32 SquadId)
            {
                Double total = Activations(TeamId, SquadId);

                //we have to visit every possible limit activation and count
                foreach (KeyValuePair<String, List<LimitEvent>> pair in activations_total)
                    if (pair.Value != null)
                        foreach (LimitEvent e in pair.Value)
                            if (e.TeamId == TeamId && e.SquadId == SquadId)
                                total++;

                return total;
            }

            public Double Activations(String PlayerName, TimeSpan time)
            {

                if (!activations.ContainsKey(PlayerName))
                    return 0;

                List<LimitEvent> events = activations[PlayerName];

                Double total = 0;
                DateTime back = DateTime.Now.Subtract(time);

                //we have to visit every possible limit activation and count
                foreach (LimitEvent e in events)
                    if (e != null && e.Time.CompareTo(back) >= 0)
                        total++;

                return total;
            }

            public Double Activations(Int32 TeamId)
            {
                Double total = 0;

                //we have to visit every possible limit activation and count
                foreach (KeyValuePair<String, List<LimitEvent>> pair in activations)
                    if (pair.Value != null)
                        foreach (LimitEvent e in pair.Value)
                            if (e.TeamId == TeamId)
                                total++;

                return total;
            }

            public Double ActivationsTotal(Int32 TeamId)
            {
                Double total = Activations(TeamId);

                //we have to visit every possible limit activation and count
                foreach (KeyValuePair<String, List<LimitEvent>> pair in activations_total)
                    if (pair.Value != null)
                        foreach (LimitEvent e in pair.Value)
                            if (e.TeamId == TeamId)
                                total++;

                return total;
            }

            public Double Spree(String PlayerName)
            {
                if (!sprees.ContainsKey(PlayerName))
                    return 0;

                return sprees[PlayerName];
            }

            public void RecordSpree(String PlayerName)
            {
                if (!sprees.ContainsKey(PlayerName))
                    sprees.Add(PlayerName, 0);

                sprees[PlayerName]++;
            }

            public void ResetSpree(String PlayerName)
            {
                if (!sprees.ContainsKey(PlayerName))
                    return;

                sprees.Remove(PlayerName);
            }

            public void ResetSprees()
            {
                sprees.Clear();
            }

            /*
            // Not needed anymore, OnJoin limits are evaluated once only in OnPlayerJoin
            public Double EvaluationsPlayer(PlayerInfoInterface player)
            {
                if (!evaluations.ContainsKey(player.Name))
                    return 0;

                return evaluations[player.Name].Count;
            }
             */

            public String fieldKeyByOffset(String name, Int32 offset)
            {
                if (!valid_fields.Contains(name))
                    return String.Empty;

                Int32 index = valid_fields.IndexOf(name) + offset;
                if (index > 0 && index < valid_fields.Count)
                    return valid_fields[index];

                return String.Empty;
            }

            public Boolean shouldSkipFieldKey(String name)
            {
                try
                {

                    if (!plugin.Agreement)
                        return true;

                    if (!isValidFieldKey(name))
                        return false;

                    String field_key = extractFieldKey(name);

                    if (Hide.Equals(ShowHide.Hide) && !field_key.Equals("hide"))
                        return true;

                    if (field_key.Equals("procon_event_type"))
                        return true;

                    if (field_key.Equals("procon_event_name"))
                        return true;

                    if (Regex.Match(field_key, @"(id|delete|last)$").Success)
                        return true;

                    if (Regex.Match(field_key, @"^evaluation_interval$").Success &&
                        !(EvaluationType.OnInterval.Equals(Evaluation) ||
                          EvaluationType.OnIntervalPlayers.Equals(Evaluation) ||
                          EvaluationType.OnIntervalServer.Equals(Evaluation)))
                        return true;

                    if (Regex.Match(field_key, @"sms_").Success &&
                        !((Action & LimitAction.SMS) > 0))
                        return true;

                    if (Regex.Match(field_key, @"tweet_").Success &&
                     !((Action & LimitAction.Tweet) > 0))
                        return true;

                    if (Regex.Match(field_key, @"mail_").Success &&
                        !((Action & LimitAction.Mail) > 0))
                        return true;

                    if (Regex.Match(field_key, @"ea_ban_").Success &&
                        !((Action & LimitAction.EABan) > 0))
                        return true;

                    if (Regex.Match(field_key, @"log_").Success &&
                        !((Action & LimitAction.Log) > 0))
                        return true;

                    if ((Regex.Match(field_key, @"log_file").Success &&
                        !((LogDestination & LimitLogDestination.File) > 0)))
                        return true;

                    if (Regex.Match(field_key, @"taskbar_notify_.+").Success &&
                        !((Action & LimitAction.TaskbarNotify) > 0))
                        return true;

                    if (Regex.Match(field_key, @"sound_notify_.+").Success &&
                        !((Action & LimitAction.SoundNotify) > 0))
                        return true;

                    if ((Regex.Match(field_key, @"second_check_.+").Success &&
                         SecondCheck.Equals(LimitType.Disabled)))
                        return true;

                    if ((Regex.Match(field_key, @"first_check_.+").Success &&
                         FirstCheck.Equals(LimitType.Disabled)))
                        return true;

                    if (Regex.Match(field_key, @"pb_ban_").Success &&
                        !((Action & LimitAction.PBBan) > 0))
                        return true;

                    if (Regex.Match(field_key, @"pb_command_").Success &&
                       !((Action & LimitAction.PBCommand) > 0))
                        return true;

                    if (Regex.Match(field_key, @"procon_event_").Success &&
                         !((Action & LimitAction.PRoConEvent) > 0))
                        return true;

                    if (Regex.Match(field_key, @"procon_chat_").Success &&
                        !((Action & LimitAction.PRoConChat) > 0))
                        return true;

                    if (Regex.Match(field_key, @"server_command_").Success &&
                        !((Action & LimitAction.ServerCommand) > 0))
                        return true;

                    if (Regex.Match(field_key, @"say_").Success &&
                        !((Action & LimitAction.Say) > 0))
                        return true;

                    if (Regex.Match(field_key, @"yell_").Success &&
                        !((Action & LimitAction.Yell) > 0))
                        return true;

                    if (Regex.Match(field_key, @"kill_").Success &&
                        !((Action & LimitAction.Kill) > 0))
                        return true;

                    if (Regex.Match(field_key, @"(kick)_").Success &&
                        !((Action & LimitAction.Kick) > 0))
                        return true;

                    if (field_key.Equals("first_check_expression") &&
                        !FirstCheck.Equals(LimitType.Expression))
                        return true;

                    if (field_key.Equals("second_check_expression") &&
                        !SecondCheck.Equals(LimitType.Expression))
                        return true;

                    if (field_key.Equals("first_check_code") &&
                        !FirstCheck.Equals(LimitType.Code))
                        return true;

                    if (field_key.Equals("second_check_code") &&
                        !SecondCheck.Equals(LimitType.Code))
                        return true;

                    if (field_key.Equals("ea_ban_minutes") &&
                        !((Action & LimitAction.EABan) > 0 &&
                          EABDuration.Equals(EABanDuration.Temporary)))
                        return true;

                    if (field_key.Equals("pb_ban_minutes") &&
                       !((Action & LimitAction.PBBan) > 0 &&
                         PBBDuration.Equals(PBBanDuration.Temporary)))
                        return true;

                }
                catch (Exception e)
                {
                    plugin.DumpException(e);
                }
                return false;

            }

            public void SetupGroups()
            {

                if (group2title == null)
                    group2title = new Dictionary<String, String>();

                if (group2regex == null)
                    group2regex = new Dictionary<String, String>();

                if (title2group == null)
                    title2group = new Dictionary<String, String>();

                group2title.Clear();
                group2regex.Clear();
                title2group.Clear();

                if ((valid_groups.Length % 3) > 0)
                {
                    plugin.ConsoleError("sanity check failed for limit field groups");
                    return;
                }

                for (Int32 i = 0; i < valid_groups.Length; i = i + 3)
                {
                    String title = valid_groups[i];
                    String group = valid_groups[i + 1];
                    String regex = valid_groups[i + 2];

                    if (!group2title.ContainsKey(group))
                        group2title.Add(group, title);

                    if (!title2group.ContainsKey(title))
                        title2group.Add(title, group);

                    if (!group2regex.ContainsKey(group))
                        group2regex.Add(group, regex);

                }
            }

            private void SetupFields()
            {
                fields = new Dictionary<String, String>();
                foreach (String field_key in valid_fields)
                    fields.Add(field_key, "");
            }

            private void InitFields(String id)
            {
                String auto_hide = (plugin.getBooleanVarValue("auto_hide_sections") ? ShowHide.Hide : ShowHide.Show).ToString();

                setFieldValue("id", id);
                setFieldValue("hide", auto_hide);
                setFieldValue("name", "Name" + id);
                setFieldValue("state", LimitState.Enabled.ToString());
                setFieldValue("evaluation", EvaluationType.OnJoin.ToString());
                setFieldValue("evaluation_interval", (10).ToString());
                setFieldValue("first_check", LimitType.Disabled.ToString());
                setFieldValue("second_check", LimitType.Disabled.ToString());
                setFieldValue("delete", (false).ToString());
                setFieldValue("new_action", LimitAction.None.ToString());
                setFieldValue("action", LimitAction.None.ToString());

                setFieldValue("kill_group", auto_hide);
                setFieldValue("kill_delay", (0).ToString());

                setFieldValue("say_group", auto_hide);
                setFieldValue("say_message", "activated " + FullReplaceName);
                setFieldValue("say_audience", MessageAudience.All.ToString());
                setFieldValue("say_procon_chat", TrueFalse.False.ToString());
                setFieldValue("say_delay", (0).ToString());

                setFieldValue("ea_ban_group", auto_hide);
                setFieldValue("ea_ban_type", EABanType.EA_GUID.ToString());
                setFieldValue("ea_ban_duration", EABanDuration.Temporary.ToString());
                setFieldValue("ea_ban_minutes", (10).ToString());
                setFieldValue("ea_ban_message", "activated " + FullReplaceName);

                setFieldValue("taskbar_notify_group", auto_hide);
                setFieldValue("taskbar_notify_title", FullReplaceName + " activation");
                setFieldValue("taskbar_notify_message", FullReplaceName + " was activated on %date%, at %time%");

                setFieldValue("sound_notify_group", auto_hide);
                setFieldValue("sound_notify_file", FullReplaceName + " activation");
                setFieldValue("sound_notify_repeat", FullReplaceName + " was activated on %date%, at %time%");

                setFieldValue("pb_ban_group", auto_hide);
                setFieldValue("pb_ban_type", PBBanType.PB_GUID.ToString());
                setFieldValue("pb_ban_duration", PBBanDuration.Temporary.ToString());
                setFieldValue("pb_ban_minutes", (10).ToString());
                setFieldValue("pb_ban_message", "activated " + FullReplaceName);

                setFieldValue("pb_command_group", auto_hide);
                setFieldValue("pb_command_text", "pb_sv_plist");

                setFieldValue("procon_chat_group", auto_hide);
                setFieldValue("procon_chat_text", "activated " + FullReplaceName);

                setFieldValue("procon_event_type", EventType.Plugins.ToString());
                setFieldValue("procon_event_name", CapturableEvent.PluginAction.ToString());
                setFieldValue("procon_event_text", "activated " + FullReplaceName);
                setFieldValue("procon_event_player", "player.Name");

                setFieldValue("server_command_group", auto_hide);
                setFieldValue("server_command_text", "admin.say \"Hello World\" all");

                setFieldValue("kick_group", auto_hide);
                setFieldValue("kick_message", "violated " + FullReplaceName);

                setFieldValue("log_group", auto_hide);
                setFieldValue("log_destination", LimitLogDestination.Plugin.ToString());
                setFieldValue("log_file", InsaneLimits.makeRelativePath("InsaneLimits.log"));
                setFieldValue("log_message", "[%date% %time%] %p_n% activated " + FullReplaceName);

                setFieldValue("sms_group", auto_hide);
                setFieldValue("sms_country", "United_States");
                setFieldValue("sms_carrier", "T-Mobile");
                setFieldValue("sms_number", "5555555555");

                setFieldValue("mail_group", auto_hide);
                setFieldValue("mail_address", "admin1@mail.com, admin2@mail.com, etc");
                setFieldValue("mail_subject", FullReplaceName + " Activation@%server_host%:%server_port% - %p_n%");

                setFieldValue("tweet_group", auto_hide);
                setFieldValue("tweet_status", "%p_n% activated " + FullReplaceName); ;

                String body = FullReplaceName + " Activation Report" + NL +
                              @"Server: %server_host%:%server_port%" + NL +
                              @"Player: %p_n%" + NL +
                              @"EA_GUID: %p_eg%" + NL +
                              @"PB_GUID: %p_pg%" + NL +
                              @"Date: %date% %time%";

                setFieldValue("mail_body", body);
                setFieldValue("sms_message", body);

                setFieldValue("yell_group", auto_hide);
                setFieldValue("yell_message", "activated " + FullReplaceName);
                setFieldValue("yell_audience", MessageAudience.All.ToString());
                setFieldValue("yell_procon_chat", TrueFalse.False.ToString());
                setFieldValue("yell_duration", (5).ToString());
            }

            private void SetupCounts()
            {
                this.activations = new Dictionary<String, List<LimitEvent>>();
                this.activations_total = new Dictionary<String, List<LimitEvent>>();
                /*
                // Not needed anymore, OnJoin limits are evaluated once only in OnPlayerJoin
                this.evaluations = new Dictionary<String, List<LimitEvent>>();
                 */
                this.sprees = new Dictionary<String, Double>();
            }

            public Limit(InsaneLimits plugin, String id)
            {
                this.plugin = plugin;

                SetupCounts();
                SetupFields();
                InitFields(id);

                SetupGroups();

                DataDict = new DataDictionary(plugin);
                RoundDataDict = new DataDictionary(plugin);

            }

            public Boolean isGroupFirstField(String var)
            {
                if (group2regex == null || group2regex.Count == 0)
                    return false;

                return group2regex.ContainsKey(extractFieldKey(var));

            }

            public String getGroupNameByKey(String key)
            {
                if (group2regex == null || group2regex.Count == 0)
                    return String.Empty;

                key = extractFieldKey(key);
                foreach (KeyValuePair<String, String> pair in group2regex)
                    if (Regex.Match(key, pair.Value, RegexOptions.IgnoreCase).Success)
                        return pair.Key;

                return String.Empty;
            }

            public String getGroupBaseTitleByKey(String key)
            {
                String name = getGroupNameByKey(key);

                if (name.Length == 0 || !group2title.ContainsKey(name))
                    return String.Empty;

                return group2title[name];
            }

            public String getGroupFormattedTitleByKey(String key)
            {

                String title = getGroupBaseTitleByKey(key);

                if (title.Length == 0)
                    return String.Empty;

                Char pchar = '-';
                //int max = 64;
                Int32 flen = 30;

                if (title.Length > flen)
                    title = title.Substring(0, flen - 2);

                Int32 spaces = flen - title.Length;
                Int32 lspace = spaces / 2;
                Int32 rspace = spaces - lspace;

                title = new String(pchar, lspace) + title + new String(pchar, rspace);

                return "[ " + id + " ]" + (new String(' ', 10)) + "[ " + title + " ]" + (new String(' ', 10));
            }

            public Boolean isValidGroupTitle(String var)
            {
                if (title2group == null || title2group.Count == 0)
                    return false;

                return title2group.ContainsKey(extractGroupBaseTitle(var));
            }

            public Boolean isValidFieldKey(String var)
            {

                if (valid_fields.Contains(extractFieldKey(var)))
                    return true;

                return false;
            }

            public static String extractGroupBaseTitle(String var)
            {
                Match match = Regex.Match(var, @"\[\s*-+\s*([^\-]+)\s*-+\s*\]");
                if (match.Success)
                    return match.Groups[1].Value;

                return var;
            }

            public static String extractFieldKey(String var)
            {
                Match match = Regex.Match(var, @"limit_[^_]+_([^0-9]+)");
                if (match.Success)
                    return match.Groups[1].Value;

                return var;
            }

            public void recompile(String field, String val, Boolean ui)
            {

                if (FirstCheck.Equals(LimitType.Disabled))
                    return;

                if ((ui && (field.Equals("evaluation") ||
                            field.Equals("first_check") ||
                            field.Equals("first_check_expression") ||
                            field.Equals("first_check_code") ||
                            field.Equals("second_check") ||
                            field.Equals("second_check_expression") ||
                            field.Equals("second_check_code"))
                    )
                   )
                {

                    plugin.CompileLimit(this);
                }
            }

            public void AccumulateActivations()
            {
                if (activations == null)
                    return;

                List<String> keys = new List<String>(activations.Keys);
                foreach (String key in keys)
                    AccumulateActivations(key);
            }

            public void AccumulateActivations(String PlayerName)
            {
                if (activations == null || !activations.ContainsKey(PlayerName))
                    return;

                List<LimitEvent> levents = null;
                activations.TryGetValue(PlayerName, out levents);
                if (levents == null || levents.Count == 0)
                    return;

                if (!activations_total.ContainsKey(PlayerName))
                    activations_total.Add(PlayerName, new List<LimitEvent>());

                activations_total[PlayerName].AddRange(levents);
            }

            public void ResetActivations(String PlayerName)
            {
                if (PlayerName == null)
                    return;

                if (!activations.ContainsKey(PlayerName))
                    return;

                activations[PlayerName].Clear();
            }

            public void ResetActivationsTotal(String PlayerName)
            {
                if (PlayerName == null)
                    return;

                if (!activations_total.ContainsKey(PlayerName))
                    return;

                activations_total[PlayerName].Clear();
            }

            public void ResetActivations()
            {
                if (activations != null)
                    activations.Clear();
            }

            public void ResetActivationsTotal()
            {
                if (activations_total != null)
                    activations_total.Clear();
            }

            /*
            // Not needed anymore, OnJoin limits are evaluated once only in OnPlayerJoin
            public void ResetEvaluations()
            {
                if (evaluations != null)
                    evaluations.Clear();
            }
             */

            /*
            // Not needed anymore, OnJoin limits are evaluated once only in OnPlayerJoin
            public void ResetEvaluations(String PlayerName)
            {
                if (evaluations == null)
                    return;

                if (!evaluations.ContainsKey(PlayerName))
                    return;

                evaluations.Remove(PlayerName);
            }
             */

            public void ResetLastInterval(DateTime now)
            {
                LastInterval = now;
            }

            public Int64 RemainingSeconds(DateTime now)
            {

                Int64 elapsed = (Int64)now.Subtract(LastInterval).TotalSeconds;

                if (elapsed >= Interval)
                {
                    ResetLastInterval(now);
                    return 0;
                }

                Int64 r = Interval - elapsed;
                plugin.DebugWrite(ShortDisplayName + " - " + Evaluation.ToString() + ", " + r + " second" + ((r > 1) ? "s" : "") + " remaining", 7);

                return r;
            }

            public void Reset()
            {
                ResetActivations();
                ResetActivationsTotal();
                ResetSprees();
                ResetLastInterval(DateTime.Now);
                Data.Clear();
            }

            public Boolean validateAndSetFieldValue(String field, String val, Boolean ui)
            {   //plugin.ConsoleWrite(field + " = " + val + ", UI: " + ui.ToString());
                if (field.Equals("delete"))
                {
                    /* Parse Boolean Values */
                    Boolean booleanValue = false;

                    if (Regex.Match(val, @"^\s*(1|true|yes)\s*$", RegexOptions.IgnoreCase).Success)
                        booleanValue = true;
                    else if (Regex.Match(val, @"^\s*(0|false|no)\s*$", RegexOptions.IgnoreCase).Success)
                        booleanValue = false;
                    else
                        return false;

                    fields[field] = booleanValue.ToString();
                }
                else if (field.Equals("state") ||
                         field.Equals("first_check") ||
                         field.Equals("second_check") ||
                         field.Equals("evaluation") ||
                         field.Equals("say_audience") ||
                         field.Equals("say_procon_chat") ||
                         field.Equals("yell_audience") ||
                         field.Equals("yell_procon_chat") ||
                         field.Equals("hide") ||
                         field.Equals("procon_event_type") ||
                         field.Equals("procon_event_name") ||
                         Regex.Match(field, "_group").Success

                    )
                {

                    /* Parse Enum */
                    Type type = null;
                    if (field.Equals("state"))
                        type = typeof(LimitState);
                    else if (Regex.Match(field, @"^(first_check|second_check)$").Success)
                        type = typeof(LimitType);
                    else if (field.Equals("evaluation"))
                        type = typeof(EvaluationType);
                    else if (field.Equals("say_audience"))
                        type = typeof(MessageAudience);
                    else if (field.Equals("say_procon_chat"))
                        type = typeof(TrueFalse);
                    else if (field.Equals("yell_audience"))
                        type = typeof(MessageAudience);
                    else if (field.Equals("yell_procon_chat"))
                        type = typeof(TrueFalse);
                    else if (field.Equals("procon_event_type"))
                        type = typeof(EventType);
                    else if (field.Equals("procon_event_name"))
                        type = typeof(CapturableEvent);
                    else if (Regex.Match(field, "_group").Success || field.Equals("hide"))
                        type = typeof(ShowHide);

                    try
                    {
                        String origValue = fields[field];

                        fields[field] = Enum.Format(type, Enum.Parse(type, val, true), "G").ToString();

                        if (field.Equals("second_check") &&
                            !SecondCheck.Equals(LimitType.Disabled) &&
                            FirstCheck.Equals(LimitType.Disabled))
                        {
                            fields[field] = LimitType.Disabled.ToString();
                            plugin.ConsoleWarn("cannot enable ^bsecond_check^n, without enabling ^bfirst_check^n for " + ShortDisplayName);
                            return false;
                        }

                        if (field.Equals("first_check") &&
                           FirstCheck.Equals(LimitType.Disabled) &&
                           !SecondCheck.Equals(LimitType.Disabled))
                        {
                            setFieldValue("second_check", LimitType.Disabled.ToString());
                            plugin.ConsoleWarn("detected that ^bfirst_check^n was disabled for " + ShortDisplayName + ", will also disable ^bsecond_check^n");
                            return true;
                        }

                        if (field.Equals("yell_audience") && fields[field].Equals("Squad"))
                        {
                            setFieldValue("yell_audience", MessageAudience.All.ToString());
                            plugin.ConsoleWarn("^byell_audience^n cannot be set to Squad, reverting to All");
                        }

                        recompile(field, val, ui);

                        // Warning for BF3 player say
                        // if (field.Equals("say_audience") && fields[field].Equals("Player"))
                        // plugin.ConsoleWarn("Battlefield 3 does not support individual player messages");

                        // Reset the activations when disbaling limits
                        if (field.Equals("state") && !Enabled)
                            Reset();

                        if (origValue != fields[field])
                        {
                            /*
                            if ((field.Equals("evaluation") && origValue != fields[field])
                            || (fields.Equals("evaluation_interval") && origValue != fields[field])) {
                            */
                            ResetLastInterval(DateTime.Now);
                        }

                        return true;
                    }
                    catch (FormatException)
                    {
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return false;
                    }

                }
                else if (Regex.Match(field, @"(id|((ea|pb)_ban_minutes)|say_delay|yell_duration|kill_delay|evaluation_interval)").Success)
                {
                    /* Parse Integer Values */
                    Int32 integerValue = 0;
                    if (!Int32.TryParse(val, out integerValue))
                        return false;

                    if (Regex.Match(field, @"(id|((ea|pb)_ban_minutes))").Success &&
                            !plugin.intAssertGTE(field, integerValue, 1))
                        return false;
                    else if (Regex.Match(field, @"(say_delay|kill_delay|yell_duration)").Success &&
                            !plugin.intAssertGTE(field, integerValue, 0))
                        return false;
                    else if (Regex.Match(field, @"^evaluation_interval$").Success &&
                            !plugin.intAssertGTE(field, integerValue, 10))
                        return false;

                    fields[field] = integerValue.ToString();
                    return true;
                }
                else if ((Regex.Match(field, @"first_check_expression").Success &&
                         FirstCheck.Equals(LimitType.Expression))

                         ||

                        (Regex.Match(field, @"first_check_code").Success &&
                         FirstCheck.Equals(LimitType.Code))

                         ||

                        (Regex.Match(field, @"second_check_code").Success &&
                         SecondCheck.Equals(LimitType.Code))

                         ||

                        (Regex.Match(field, @"second_check_expression").Success &&
                         SecondCheck.Equals(LimitType.Expression))
                       )
                {
                    fields[field] = val;
                    recompile(field, val, ui);
                    return true;
                }
                else if (field.Equals("action") && ui)
                {
                    ActionsList = CleanupActions(val);
                    return false;
                }
                else if (ui && field.Equals("new_action"))
                {
                    try
                    {
                        if (Str2Action(val).Equals(LimitAction.None))
                            ActionsList = new List<String>(new String[] { LimitAction.None.ToString() });
                        else
                            ActionsList = CleanupActions(fields["action"] + "|" + val);

                    }
                    catch (Exception)
                    { }

                    fields[field] = "...";

                    return false;
                }
                else
                    fields[field] = val;

                return true;
            }

            public Boolean setFieldValue(String var, String val)
            {
                return setFieldValue(var, val, false);
            }

            public Boolean getGroupStateByTitle(String title)
            {

                title = extractGroupBaseTitle(title);

                if (title2group == null || title2group.Count == 0 || !isValidGroupTitle(title))
                    return false;

                if (!title2group.ContainsKey(title))
                    return false;

                String state = getField(title2group[title]);
                try
                {
                    return Enum.Parse(typeof(ShowHide), state).Equals(ShowHide.Show);
                }
                catch (Exception)
                { }

                return true;
            }

            public Boolean getGroupStateByKey(String key)
            {

                String title = getGroupBaseTitleByKey(extractFieldKey(key));
                if (title.Length == 0)
                    return true;

                return getGroupStateByTitle(title);
            }

            public Boolean setGroupStateByTitle(String title, String val, Boolean ui)
            {

                title = extractGroupBaseTitle(title);

                if (!isValidGroupTitle(title))
                    return false;

                if (!title2group.ContainsKey(title))
                    return false;

                return setFieldValue(title2group[title], val, ui);

            }

            public Boolean setFieldValue(String var, String val, Boolean ui)
            {
                //plugin.ConsoleWrite("Setting: " +var +" = " + val);
                String field_key = extractFieldKey(var);
                if (!isValidFieldKey(field_key))
                    return false;

                return validateAndSetFieldValue(field_key, val, ui);

            }

            public String getField(String var)
            {
                if (!isValidFieldKey(var))
                    return "";

                String field_key = extractFieldKey(var);
                return fields[field_key];
            }

            public static Boolean isLimitVar(String var)
            {

                if (Regex.Match(var, @"^limit_[^_]+_(" + String.Join("|", valid_fields.ToArray()) + ")").Success)
                    return true;

                if (Regex.Match(var, @"^\s*\[\s*[^ \]]+\s*\]", RegexOptions.IgnoreCase).Success)
                    return true;

                return false;
            }

            public static String extractId(String var)
            {
                Match vmatch = Regex.Match(var, @"^limit_([^_]+)");
                if (vmatch.Success)
                    return vmatch.Groups[1].Value;

                Match hmatch = Regex.Match(var, @"^\s*\[\s*([^ \]]+)\s*\]", RegexOptions.IgnoreCase);
                if (hmatch.Success)
                    return hmatch.Groups[1].Value;

                return "UnknownId";
            }

            public Dictionary<String, String> getSettings(Boolean display)
            {

                Dictionary<String, String> settings = new Dictionary<String, String>();

                /* optimization */
                if (display && Hide.Equals(ShowHide.Hide))
                {
                    settings.Add("limit_" + id + "_hide", Hide.ToString());
                    return settings;
                }

                List<String> keys = new List<String>(fields.Keys);
                for (Int32 i = 0; i < keys.Count; i++)
                {
                    String key = keys[i];
                    if (!fields.ContainsKey(key))
                        continue;

                    String value = fields[key];

                    settings.Add("limit_" + id + "_" + key, value);
                }

                return settings;
            }
        }
    }

    public class StatsException : Exception
    {
        public Int32 code = 0;
        public WebException web_exception = null;
        public StatsException(String message) : base(message) { }
        public StatsException(String message, Int32 code) : base(message) { this.code = code; }
        public StatsException(String message, String url) : base(message + " (" + url + ")") { }
    }

    public class TwitterException : Exception
    {
        public Int32 code = 0;
        public TwitterException(String message) : base(message) { }
        public TwitterException(String message, Int32 code) : base(message) { this.code = code; }
    }

    public class CompileException : Exception
    {
        public CompileException(String message) : base(message) { }
    }

    public class EvaluationException : Exception
    {
        public EvaluationException(String message) : base(message) { }
    }

    public class BattleLog
    {
        private InsaneLimits plugin = null;

        //private HttpWebRequest req = null;
        //private CookieContainer cookies = null;

        private GZipWebClient client = null;
        private String curAddress = "";

        public class GZipWebClient : WebClient
        {
            private String ua;
            private Boolean compress;

            public GZipWebClient()
            {
                this.ua = "Mozilla/5.0 (compatible; PRoCon 1; Insane Limits)";
                base.Headers["User-Agent"] = ua;
                compress = true;
            }

            public GZipWebClient(Boolean compress) : this()
            {
                this.compress = compress;
            }

            public String GZipDownloadString(String address)
            {
                return this.GZipDownloadString(new Uri(address));
            }

            public String GZipDownloadString(Uri address)
            {
                base.Headers[HttpRequestHeader.UserAgent] = ua;

                if (compress == false)
                    return base.DownloadString(address);

                base.Headers[HttpRequestHeader.AcceptEncoding] = "gzip";
                var stream = this.OpenRead(address);
                if (stream == null)
                    return "";

                var contentEncoding = ResponseHeaders[HttpResponseHeader.ContentEncoding];
                base.Headers.Remove(HttpRequestHeader.AcceptEncoding);

                Stream decompressedStream = null;
                StreamReader reader = null;
                if (!String.IsNullOrEmpty(contentEncoding) && contentEncoding.ToLower().Contains("gzip"))
                {
                    decompressedStream = new GZipStream(stream, CompressionMode.Decompress);
                    reader = new StreamReader(decompressedStream);
                }
                else
                {
                    reader = new StreamReader(stream);
                }
                var data = reader.ReadToEnd();
                reader.Close();
                decompressedStream?.Close();
                stream.Close();
                return data;
            }

            public void SetProxy(String proxyURL)
            {
                if (!String.IsNullOrEmpty(proxyURL))
                {
                    Uri uri = new Uri(proxyURL);
                    this.Proxy = new WebProxy(proxyURL, true);
                    if (!String.IsNullOrEmpty(uri.UserInfo))
                    {
                        String[] parameters = uri.UserInfo.Split(':');
                        if (parameters.Length < 2)
                        {
                            return;
                        }
                        this.Proxy.Credentials = new NetworkCredential(parameters[0], parameters[1]);
                    }
                }
            }
        }

        public BattleLog(InsaneLimits plugin)
        {
            this.plugin = plugin;

        }

        public void CleanUp()
        {
            client = null; // Release WebClient to avoid re-use error
            curAddress = "";
        }

        private String fetchWebPage(ref String html_data, String url)
        {
            try
            {
                if (client == null)
                {
                    curAddress = null;
                    client = new GZipWebClient();
                    // XXX String ua = "Mozilla/5.0 (compatible; MSIE 9.0; Windows NT 6.1; WOW64; Trident/5.0; .NET CLR 3.5.30729)";
                }

                // proxy support
                if (plugin.getBooleanVarValue("use_battlelog_proxy"))
                {
                    // set proxy
                    try
                    {
                        var address = plugin.getStringVarValue("proxy_url");
                        if (curAddress == null || client.Proxy == null || !curAddress.Equals(address))
                        {
                            client.SetProxy(address);
                            curAddress = address;
                        }
                    }
                    catch (UriFormatException)
                    {
                        plugin.ConsoleError("Invalid Proxy URL set!");
                    }
                }

                DateTime since = DateTime.Now;

                html_data = client.GZipDownloadString(url);

                /* TESTS
                String testUrl = "http://status.savanttools.com/?code=";
                html_data = client.DownloadString(testUrl + "429%20Too%20Many%20Requests");
                //html_data = client.DownloadString(testUrl + "509%20Bandwidth%20Limit%20Exceeded");
                //html_data = client.DownloadString(testUrl + "408%20Request%20Timeout");
                //html_data = client.DownloadString(testUrl + "404%20Not%20Found");
                */

                plugin.DebugWrite("^2^bTIME^n took " + DateTime.Now.Subtract(since).TotalSeconds.ToString("F2") + " secs, fetchWebPage: " + url, 5);

                if (Regex.Match(html_data, @"that\s+page\s+doesn't\s+exist", RegexOptions.IgnoreCase | RegexOptions.Singleline).Success)
                    throw new StatsException("^b" + url + "^n does not exist", 404);

                return html_data;

            }
            catch (WebException e)
            {
                client = null; // release WebClient
                if (e.Status.Equals(WebExceptionStatus.Timeout))
                {
                    StatsException se = new StatsException("HTTP request timed-out");
                    se.web_exception = e;
                    throw se;
                }
                else
                {
                    throw;
                }
            }
            catch (Exception ae)
            {
                client = null; // release WebClient
                throw ae;
            }
            //return html_data;
        }

        private Boolean CheckSuccess(Hashtable json, out StatsException statsEx)
        {
            String m;

            if (json == null)
            {
                m = "JSON response is null!";
                plugin.DebugWrite(m, 5);
                statsEx = new StatsException(m);
                return false;
            }

            if (!json.ContainsKey("type"))
            {
                m = "JSON response malformed: does not contain 'type'!";
                plugin.DebugWrite(m, 5);
                statsEx = new StatsException(m);
                return false;
            }

            String type = (String)json["type"];

            if (type == null)
            {
                m = "JSON response malformed: 'type' is null!";
                plugin.DebugWrite(m, 5);
                statsEx = new StatsException(m);
                return false;
            }

            if (Regex.Match(type, @"success", RegexOptions.IgnoreCase).Success)
            {
                statsEx = null;
                return true;
            }

            if (!json.ContainsKey("message"))
            {
                m = "JSON response malformed: does not contain 'message'!";
                plugin.DebugWrite(m, 5);
                statsEx = new StatsException(m);
                return false;
            }

            String message = (String)json["message"];

            if (message == null)
            {
                m = "JSON response malformed: 'message' is null!";
                plugin.DebugWrite(m, 5);
                statsEx = new StatsException(m);
                return false;
            }

            m = "Cache fetch failed (type: " + type + ", message: " + message + ")!";
            plugin.DebugWrite(m, 5);
            statsEx = new StatsException(m);
            return false;
        }

        private String fetchJSON(ref String bigText, String url, String playerName, String requestType)
        {
            Boolean directFetchEnabled = plugin.getBooleanVarValue("use_direct_fetch");
            Boolean cacheEnabled = plugin.IsCacheEnabled(false);
            Boolean ok = false;

            bigText = String.Empty;

            if (cacheEnabled)
            {
                // block waiting for cache to respond
                bigText = plugin.SendCacheRequest(playerName, requestType);
                ok = !String.IsNullOrEmpty(bigText);
                if (ok) return String.Empty;
                // if !ok, fall back on direct fetch, if enabled
            }

            if (!ok && directFetchEnabled && url != null)
            {
                return fetchWebPage(ref bigText, url);
            }

            if (url == null && requestType == "clanTag")
            {
                return String.Empty; // caller may try direct
            }

            // Unable to fetch JSON
            plugin.DebugWrite("Unable to fetch stats for " + playerName + ", caching is disabled and direct fetch is disabled!", 4);
            throw new StatsException("stats fetching is disabled");

            //return String.Empty;
        }

        public PlayerInfo fetchStats(PlayerInfo pinfo)
        {
            try
            {
                Boolean directFetchEnabled = plugin.getBooleanVarValue("use_direct_fetch");
                Boolean cacheEnabled = plugin.IsCacheEnabled(false);

                String player = pinfo.Name;
                String result = String.Empty;
                String personaId = String.Empty;
                Hashtable json = null;
                //String type = null;
                //String message = null;
                StatsException statsEx = null;
                Hashtable data = null;

                if (!cacheEnabled && !directFetchEnabled)
                {
                    throw new StatsException("Unable to fetch stats for " + player + ", cache is disabled and direct fetching is disabled!");
                }

                /* First fetch the player's main page to get the persona id */

                Boolean okClanTag = false;
                if (cacheEnabled)
                {
                    /* Get clan tag from cache */
                    fetchJSON(ref result, null, player, "clanTag");

                    json = (Hashtable)JSON.JsonDecode(result);

                    if (!CheckSuccess(json, out statsEx)) throw statsEx;

                    /* verify there is data structure */
                    Hashtable d = null;
                    if (!json.ContainsKey("data") || (d = (Hashtable)json["data"]) == null)
                        throw new StatsException("JSON clanTag response does not contain a ^bdata^n field, for " + player);

                    if (!d.ContainsKey("clanTag"))
                        throw new StatsException("JSON clanTag response does not contain a ^bclanTag^n field, for " + player);

                    String t = (String)d["clanTag"];
                    if (!String.IsNullOrEmpty(t)) pinfo.tag = t;
                    okClanTag = true;
                }

                if (!okClanTag && directFetchEnabled)
                {
                    if (!plugin.plugin_enabled)
                    {
                        throw new StatsException("fetchStats aborted, disabling plugin ...");
                    }

                    String purl = null;
                    if (plugin.game_version == "BFHL")
                    {
                        purl = "http://battlelog.battlefield.com/bfh/user/";
                    }
                    else if (plugin.game_version == "BF4")
                    {
                        purl = "http://battlelog.battlefield.com/bf4/user/";
                    }
                    else
                    {
                        purl = "http://battlelog.battlefield.com/bf3/user/";
                    }

                    fetchWebPage(ref result, purl + player);

                    if (!plugin.plugin_enabled)
                    {
                        throw new StatsException("fetchStats aborted, disabling plugin ...");
                    }

                    /* Extract the persona id */
                    MatchCollection pid = null;
                    Match spid = null;

                    if (plugin.game_version == "BFHL")
                    {
                        spid = Regex.Match(result, @"agent\/" + player + @"\/stats\/(\d+)");
                    }
                    else if (plugin.game_version == "BF4")
                    {
                        pid = Regex.Matches(result, @"bf4/soldier/" + player + @"/stats/(\d+)(['""]|/\s*['""]|/[^/'""]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    }
                    else
                    {
                        pid = Regex.Matches(result, @"bf3/soldier/" + player + @"/stats/(\d+)(['""]|/\s*['""]|/[^/'""]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    }

                    if (spid == null)
                    {
                        foreach (Match match in pid)
                            if (match.Success && !Regex.Match(match.Groups[2].Value.Trim(), @"(ps3|xbox)", RegexOptions.IgnoreCase).Success)
                            {
                                personaId = match.Groups[1].Value.Trim();
                                break;
                            }
                    }
                    else
                    {
                        if (spid.Success)
                        {
                            personaId = spid.Groups[1].Value.Trim();
                        }
                    }

                    if (String.IsNullOrEmpty(personaId))
                        throw new StatsException("could not find persona-id for ^b" + player + "^n");

                    if (plugin.game_version == "BFHL")
                    {
                        // Get the stats page
                        if (!plugin.plugin_enabled) return pinfo;
                        String bfhfurl = "http://battlelog.battlefield.com/bfh/agent/" + player + "/stats/" + personaId + "/pc/" + "?nocacherandom=" + Environment.TickCount;
                        fetchWebPage(ref result, bfhfurl);
                        if (!plugin.plugin_enabled) return pinfo;

                        // Extract the player tag
                        String bfhTag = String.Empty;
                        Match tag = Regex.Match(result, @"\[\s*([a-zA-Z0-9]+)\s*\]\s*</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

                        if (tag.Success)
                        {
                            bfhTag = tag.Groups[1].Value.Trim();
                        }
                        if (String.IsNullOrEmpty(bfhTag))
                        {
                            // No tag
                            pinfo.tag = String.Empty;
                            plugin.DebugWrite("^4Battlelog says ^b" + player + "^n has no BFHL tag", 5);
                        }
                        else
                        {
                            pinfo.tag = bfhTag;
                        }
                    }
                    else if (plugin.game_version == "BF4")
                    {

                        if (!plugin.plugin_enabled)
                        {
                            throw new StatsException("fetchStats aborted, disabling plugin ...");
                        }

                        String turl = "http://battlelog.battlefield.com/bf4/warsawoverviewpopulate/" + personaId + "/1/";
                        fetchWebPage(ref result, turl);

                        if (!plugin.plugin_enabled)
                        {
                            throw new StatsException("fetchStats aborted, disabling plugin ...");
                        }

                        json = (Hashtable)JSON.JsonDecode(result);

                        // check we got a valid response

                        /* verify we got a success message */
                        if (!CheckSuccess(json, out statsEx)) throw statsEx;

                        /* verify there is data structure */
                        if (!json.ContainsKey("data") || (data = (Hashtable)json["data"]) == null)
                            throw new StatsException("JSON response does not contain a ^bdata^n field, for " + player, turl);

                        // verify there is viewedPersonaInfo structure, okay if null!
                        Hashtable info = null;
                        if (!data.ContainsKey("viewedPersonaInfo") || (info = (Hashtable)data["viewedPersonaInfo"]) == null)
                        {
                            // No tag
                            pinfo.tag = String.Empty;
                            plugin.DebugWrite("Battlelog says ^b" + player + "^n has no BF4 tag (no viewedPersonaInfo)", 5);
                        }
                        else
                        {
                            // Extract the player tag
                            String bf4Tag = String.Empty;
                            if (!info.ContainsKey("tag") || String.IsNullOrEmpty(bf4Tag = (String)info["tag"]))
                            {
                                // No tag
                                pinfo.tag = String.Empty;
                                plugin.DebugWrite("^4Battlelog says ^b" + player + "^n has no BF4 tag", 5);
                            }
                            else
                            {
                                pinfo.tag = bf4Tag;
                            }
                        }
                    }
                    else
                    {
                        extractClanTag(result, pinfo);
                    }
                }

                /* Next, get player's overview stats */

                if (!plugin.plugin_enabled)
                {
                    throw new StatsException("fetchStats aborted, disabling plugin ...");
                }

                String furl = null;
                if (plugin.game_version == "BFHL")
                {
                    furl = "http://battlelog.battlefield.com/bfh/warsawdetailedstatspopulate/" + personaId + "/1/";
                }
                else if (plugin.game_version == "BF4")
                {
                    furl = "http://battlelog.battlefield.com/bf4/warsawdetailedstatspopulate/" + personaId + "/1/";
                }
                else
                {
                    furl = "http://battlelog.battlefield.com/bf3/overviewPopulateStats/" + personaId + "/bf3-us-engineer/1/";
                }
                fetchJSON(ref result, furl, player, "overview");

                if (!plugin.plugin_enabled)
                {
                    throw new StatsException("fetchStats aborted, disabling plugin ...");
                }

                json = (Hashtable)JSON.JsonDecode(result);

                // check we got a valid response

                /* verify we got a success message */
                if (!CheckSuccess(json, out statsEx)) throw statsEx;

                /* verify there is data structure */
                if (!json.ContainsKey("data") || (data = (Hashtable)json["data"]) == null)
                    throw new StatsException("JSON response does not contain a ^bdata^n field, for " + player, furl);

                /* verify there is stats structure */
                Hashtable stats = null;
                String jsonOverviewStatsKey = (plugin.game_version == "BF3") ? "overviewStats" : "generalStats";
                if (!data.ContainsKey(jsonOverviewStatsKey) || (stats = (Hashtable)data[jsonOverviewStatsKey]) == null)
                    throw new StatsException("JSON response ^bdata^n does not contain ^b" + jsonOverviewStatsKey + "^n, for " + player, furl);

                /* extract the fields from the stats */
                extractBasicFields(stats, pinfo);

                /* verify there is a kitmap structure */
                Hashtable kitMap = null;
                if (!data.ContainsKey("kitMap") || (kitMap = (Hashtable)data["kitMap"]) == null)
                {
                    if (plugin.game_version == "BF3")
                        throw new StatsException("JSON response ^bdata^n does not contain ^bkitMap^n, for " + player, furl);
                    else
                    {
                        kitMap = null;
                    }
                }

                /* Build the id->kit and kit->id maps */
                List<Dictionary<String, String>> maps = null;
                //Dictionary<String, String> kit2id = null;
                Dictionary<String, String> id2kit = null;
                if (kitMap != null)
                {
                    maps = buildKitMaps(kitMap);
                    //kit2id = maps[1];
                    id2kit = maps[1];
                }
                else
                {
                    id2kit = new Dictionary<String, String>();
                    id2kit["1"] = "assault";
                    id2kit["2"] = "engineer";
                    id2kit["8"] = "recon";
                    id2kit["16"] = "vehicle";
                    id2kit["32"] = "support";
                    id2kit["64"] = "general";
                    id2kit["2048"] = "commander";
                }

                /* verify there is kit times (seconds) structure */
                Hashtable kitTimes = null;
                if (!stats.ContainsKey("kitTimes") || (kitTimes = (Hashtable)stats["kitTimes"]) == null)
                    throw new StatsException("JSON response ^boverviewStats^n does not contain ^bkitTimes^n, for " + player, furl);

                /*  extract the kit times (seconds) */
                extractKitTimes(kitTimes, id2kit, pinfo, "_t");

                /* verify there is kit time (percent) structure */
                Hashtable kitTimesInPercentage = null;
                if (!stats.ContainsKey("kitTimesInPercentage") || (kitTimesInPercentage = (Hashtable)stats["kitTimesInPercentage"]) == null)
                    throw new StatsException("JSON response ^boverviewStats^n does not contain ^bkitTimesInPercentage^n, for " + player, furl);

                /*  extract the kit times (percentage) */
                extractKitTimes((Hashtable)stats["kitTimesInPercentage"], id2kit, pinfo, "_p");

                if (!plugin.plugin_enabled)
                {
                    throw new StatsException("fetchStats aborted, disabling plugin ...");
                }

                DateTime since = DateTime.Now;

                try
                {

                    String logName = @"Logs\" + plugin.server_host + "_" + plugin.server_port + @"\" + DateTime.Now.ToString("yyyyMMdd") + "_battle.log";

                    /* print the collected stats to log */
                    if (plugin.getBooleanVarValue("use_stats_log"))
                    {
                        since = DateTime.Now;

                        plugin.Log(logName, "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + pinfo.FullName + ((okClanTag && cacheEnabled) ? " Battlelog CACHED stats: " : " Battlelog player stats:"));
                        pinfo.dumpStatProperties("web", logName);

                        if (DateTime.Now.Subtract(since).TotalSeconds > 1) plugin.DebugWrite("^2^bTIME^n took " + DateTime.Now.Subtract(since).TotalSeconds.ToString("F2") + " secs, dumpStatProperties", 5);
                    }

                    /* extract weapon level statistics */
                    List<BattlelogWeaponStats> wstats = null;
                    if (plugin.getBooleanVarValue("use_slow_weapon_stats") && plugin.game_version != "BFHL")
                    {
                        wstats = extractWeaponStats(pinfo, personaId);
                    }
                    else
                    {
                        plugin.DebugWrite("^1^buse_slow_weapon_stats^n is ^bFalse^n or BFHL, skipping fetch of weapon stats", 5);
                    }

                    pinfo.BWS.setWeaponData(wstats);

                    if (!plugin.plugin_enabled)
                    {
                        throw new StatsException("fetchStats aborted, disabling plugin ...");
                    }

                    if (wstats != null && plugin.getBooleanVarValue("use_stats_log"))
                    {
                        since = DateTime.Now;

                        String bwsBlob = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + pinfo.FullName + " Battlelog weapon stats:\n";
                        foreach (BattlelogWeaponStats bws in wstats)
                        {
                            bwsBlob = bwsBlob + "    N:" + bws.Name + ", S:" + bws.Slug + ", C:" + bws.Category + ", Code:" + bws.Code + ", K:" + bws.Kills.ToString("F0") + ", SF:" + bws.ShotsFired.ToString("F0") + ", SH:" + bws.ShotsHit.ToString("F0") + ", A:" + bws.Accuracy.ToString("F3") + ", HS:" + bws.Headshots.ToString("F0") + ", T:" + TimeSpan.FromSeconds(bws.TimeEquipped).ToString() + "\n";
                        }
                        bwsBlob = bwsBlob + "=====================\n";
                        plugin.AppendData(bwsBlob, logName); // raw version of Log()

                        if (DateTime.Now.Subtract(since).TotalSeconds > 1) plugin.DebugWrite("^2^bTIME^n took " + DateTime.Now.Subtract(since).TotalSeconds.ToString("F2") + " secs, log weapon stats", 5);
                    }
                    plugin.DebugWrite("done logging stats for " + pinfo.Name, 5);

                }
                catch (Exception e)
                {
                    throw e;
                }
                finally
                {
                    pinfo.StatsError = false;
                }
            }
            catch (StatsException e)
            {
                if (e.web_exception == null)
                {
                    if (plugin.getIntegerVarValue("debug_level") >= 4) plugin.ConsoleWarn("(StatsException) " + e.Message);
                }
                else
                {
                    plugin.DebugWrite("(StatsException) System.Net.WebException: " + e.web_exception.Message, 4);
                    pinfo._web_exception = e.web_exception;
                }

                if (e.code == 404)
                {
                    pinfo.Battlelog404 = true;
                }

                pinfo.StatsError = true;
            }
            catch (System.Net.WebException e)
            {
                plugin.DebugWrite("System.Net.WebException: " + e.Message, 4);
                pinfo.StatsError = true;
                pinfo._web_exception = e;
            }
            catch (Exception e)
            {
                pinfo.StatsError = true;
                plugin.DumpException(e);
            }
            finally
            {
                // Clean-up the cache response, if any
                Boolean didit = false;
                if (pinfo != null && pinfo.Name != null)
                {
                    lock (plugin.cacheResponseTable)
                    {
                        if (plugin.cacheResponseTable.ContainsKey(pinfo.Name))
                        {
                            plugin.cacheResponseTable.Remove(pinfo.Name);
                            didit = true;
                        }
                    }
                    if (didit)
                    {
                        plugin.DebugWrite("Finally cleaned up cacheResponseTable for " + pinfo.Name, 4);
                    }
                }
            }

            return pinfo;
        }

        public List<BattlelogWeaponStats> extractWeaponStats(PlayerInfo pinfo, String personaId)
        {
            /* extract per-weapon stats */
            String result = String.Empty;
            String player = pinfo.Name;
            Hashtable json = null;
            StatsException statsEx = null;

            if (!plugin.plugin_enabled)
            {
                throw new StatsException("fetchStats aborted, disabling plugin ...");
            }

            String furl = null;

            if (plugin.game_version == "BF4")
            {
                furl = "http://battlelog.battlefield.com/bf4/warsawWeaponsPopulateStats/" + personaId + "/1/";
            }
            else
            {
                furl = "http://battlelog.battlefield.com/bf3/weaponsPopulateStats/" + personaId + "/1";
            }
            fetchJSON(ref result, furl, player, "weapon");

            json = (Hashtable)JSON.JsonDecode(result);

            result = null;

            // check we got a valid response

            /* verify we got a success message */
            if (!CheckSuccess(json, out statsEx)) throw statsEx;

            /* verify there is data structure */
            Hashtable data = null;
            if (!json.ContainsKey("data") || (data = (Hashtable)json["data"]) == null)
                throw new StatsException("JSON weapon response was does not contain a ^bdata^n field, for " + player, furl);

            /* verify there is stats structure */
            ArrayList wstats = null;
            if (!data.ContainsKey("mainWeaponStats") || (wstats = (ArrayList)data["mainWeaponStats"]) == null)
                throw new StatsException("JSON weapon response ^bdata^n does not contain ^bmainWeaponStats^n, for " + player, furl);

            Int32 count = 0;
            Type dtype = typeof(BattlelogWeaponStats);
            List<PropertyInfo> props = new List<PropertyInfo>(dtype.GetProperties());

            List<BattlelogWeaponStats> all_weapons = new List<BattlelogWeaponStats>();
            foreach (Object item in wstats)
            {

                if (!plugin.plugin_enabled)
                {
                    throw new StatsException("fetchStats aborted, disabling plugin ...");
                }

                String itemName = "(item " + all_weapons.Count.ToString() + ")";

                try
                {
                    if (item == null || !item.GetType().Equals(typeof(Hashtable)))
                        throw new Exception("weapon item invalid");

                    Hashtable wstat = null;
                    if ((wstat = (Hashtable)item) == null)
                        throw new Exception("weapon item null");

                    BattlelogWeaponStats bwstats = new BattlelogWeaponStats();

                    String ttmp = null;
                    if (!wstat.ContainsKey("name") || (ttmp = (String)wstat["name"]) != null)
                        itemName = ttmp;

                    List<String> keys = InsaneLimits.getBasicWJSONFieldKeys();
                    Boolean failed = false;
                    foreach (String key in keys)
                    {
                        if (!wstat.ContainsKey(key) || wstat[key] == null)
                        {
                            // For BF4, knife and similar weapons don't have the "headshots" property, so add a dummy
                            if (plugin.game_version == "BF4" && key == "headshots")
                            {
                                wstat[key] = 0.0;
                            }
                            else
                            {
                                plugin.DebugWrite("JSON structure of weapon stat for ^b" + itemName + "^n does not contain ^b" + key + "^n, for " + player, 5);
                                failed = true;
                                break;
                            }
                        }

                        String pname = InsaneLimits.WJSON2Prop(key);
                        PropertyInfo prop = null;
                        if ((prop = dtype.GetProperty(pname)) == null)
                        {
                            plugin.DebugWrite(dtype.Name + " does not contain ^b" + pname + "^n property, for " + player, 5);
                            failed = true;
                            break;
                        }

                        Type ptype = prop.PropertyType;

                        Object value = wstat[key];

                        MethodInfo castMethod = this.GetType().GetMethod("Cast").MakeGenericMethod(ptype);
                        Object castedObject = castMethod.Invoke(null, new Object[] { value });

                        prop.SetValue((Object)bwstats, castedObject, null);

                    }

                    // skip this weapon
                    if (failed)
                        continue;

                    all_weapons.Add(bwstats);

                }
                catch (Exception)
                {
                    count++;
                    plugin.DebugWrite("Battlelog weapon stats parse of ^b" + itemName + "^n failed, skipping ...", 5);
                    continue;
                }
            }

            if (count > 0)
                plugin.DebugWrite("could not parse " + count + " weapon" + ((count > 1) ? "s" : "") + " for ^b" + pinfo.Name + "^n", 4);

            return all_weapons;
        }

        public static T Cast<T>(Object data)
        {
            return (T)data;
        }

        public void extractBasicFields(Hashtable stats, PlayerInfo pinfo)
        {
            try
            {
                if (stats == null)
                {
                    plugin.DebugWrite("extractBasicFields, overviewStats Hashtable is null", 5);
                    return;
                }
                List<String> keys = InsaneLimits.getBasicJSONFieldKeys(plugin.game_version);
                foreach (DictionaryEntry entry in stats)
                {
                    String entry_key = (String)(entry.Key.ToString());

                    try
                    {

                        /* skip entries we are not interested in */
                        if (!keys.Contains(entry_key))
                            continue;

                        String entry_value = (String)(entry.Value.ToString());

                        Double dValue = Double.NaN;
                        if (Double.TryParse(entry_value, out dValue))
                            pinfo.ovalue[InsaneLimits.JSON2Key(entry_key, plugin.game_version)] = dValue;
                    }
                    catch (Exception e)
                    {
                        plugin.DebugWrite("^1^bNOTE^n^0: overviewStats problem with key ^b" + entry_key + "^n: " + e.Message, 5);
                    }
                }
                // After June 2013 Battlelog stats update, need to fix up kdRatio
                Double kills = 0;
                Double deaths = 0;
                if (Double.IsNaN(pinfo.ovalue[InsaneLimits.JSON2Key("kdRatio", plugin.game_version)])
                && !Double.IsNaN(kills = pinfo.ovalue[InsaneLimits.JSON2Key("kills", plugin.game_version)])
                && !Double.IsNaN(deaths = pinfo.ovalue[InsaneLimits.JSON2Key("deaths", plugin.game_version)]))
                {
                    deaths = (deaths == 0) ? 1 : deaths;
                    pinfo.ovalue[InsaneLimits.JSON2Key("kdRatio", plugin.game_version)] = (kills / deaths);
                }
            }
            catch (Exception e)
            {
                plugin.DebugWrite("^1^bNOTE^n^0: extractBasicFields problem: " + e.Message, 5);
            }
        }

        public void extractClanTag(String result, PlayerInfo pinfo)
        {
            /* Extract the player tag */
            Match tag = Regex.Match(result, @"\[\s*([a-zA-Z0-9]+)\s*\]\s*" + pinfo.Name, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (tag.Success)
                pinfo.tag = tag.Groups[1].Value;
        }

        public List<Dictionary<String, String>> buildKitMaps(Hashtable kitMap)
        {
            List<Dictionary<String, String>> maps = new List<Dictionary<String, String>>();

            Dictionary<String, String> kit2id = new Dictionary<String, String>();
            Dictionary<String, String> id2kit = new Dictionary<String, String>();
            kit2id.Add("vehicle", null);
            kit2id.Add("support", null);
            kit2id.Add("assault", null);
            kit2id.Add("engineer", null);
            kit2id.Add("recon", null);

            foreach (String id in kitMap.Keys)
            {
                Hashtable kit_detail = (Hashtable)kitMap[id];
                if (!kit_detail.ContainsKey("name"))
                    continue;

                /* extract the kit name */
                String kit = (String)(kit_detail["name"]).ToString();

                if (kit2id.ContainsKey(kit))
                {
                    kit2id[kit] = id;

                    if (!id2kit.ContainsKey(id))
                        id2kit.Add(id, kit);
                }
            }

            /* verify we found the ids for all kits */
            foreach (KeyValuePair<String, String> pair in kit2id)
                if (pair.Value == null)
                    throw new StatsException("could not find ^b" + pair.Key + "^n in the ^bkitMap^n");

            maps.Add(kit2id);
            maps.Add(id2kit);

            return maps;
        }

        public void extractKitTimes(Hashtable table, Dictionary<String, String> id2kit, PlayerInfo pinfo, String suffix)
        {
            if (suffix != null)
            {
                foreach (DictionaryEntry entry in table)
                {
                    String key = (String)(entry.Key).ToString();
                    String value = (String)(table[key]).ToString();

                    /* skip the ones we are not interested in */
                    if (!id2kit.ContainsKey(key))
                        continue;

                    String kit_name = id2kit[key];

                    Double dValue = Double.NaN;
                    if (Double.TryParse(value, out dValue))
                        pinfo.ovalue[kit_name + suffix] = dValue;
                }
            }
        }

    }

    public class KillInfo : KillInfoInterface
    {
        public Kill kill;
        public BaseEvent type;
        public String _category;

        public DateTime _time = DateTime.Now;

        public String Weapon { get { return kill.DamageType; } }
        public Boolean Headshot { get { return kill.Headshot; } }
        public DateTime Time { get { return _time; } }
        public String Category { get { return _category; } }

        public KillInfo(Kill kill, BaseEvent type, String category)
        {
            this.kill = kill;
            this.type = type;
            this._category = category;
        }

    }

    public class KillReason : KillReasonInterface
    {
        public String _name = String.Empty;
        public String _detail = null;
        public String _attachedTo = null;
        public String _vName = null;
        public String _vDetail = null;

        public String Name { get { return _name; } } // weapon name or reason, like "Suicide"
        public String Detail { get { return _detail; } } // BF4: ammo or attachment
        public String AttachedTo { get { return _attachedTo; } } // BF4: main weapon when Name is a secondary attachment, like M320
        public String VehicleName { get { return _vName; } } // BF4: if Name is "Death", this is the vehicle's name
        public String VehicleDetail { get { return _vDetail; } } // BF4: if Name is "Death", this is the vehicle's detail (stuff after final slash)

        public KillReason() { }
        /*
        public KillReason(String name, String detail, String attachedTo)
        {
            _name = name;
            _detail = detail;
            _attachedTo = attachedTo;
        }
        public KillReason(String name, String detail, String attachedTo, String vehicleName, String vehicleDetail)
        {
            _name = name;
            _detail = detail;
            _attachedTo = attachedTo;
            _vName = vehicleName;
            _vDetail = vehicleDetail;
        }
        */
    }

    public class ServerInfo : ServerInfoInterface
    {
        InsaneLimits plugin = null;
        public CServerInfo data = null;
        List<MaplistEntry> mlist = null;
        List<TeamScore> _TeamTickets = null;
        Dictionary<Int32, Double> _StartTickets = null;
        public Int32[] _Faction = null;

        List<String> _mapRotation = new List<String>();
        List<String> _modeRotation = new List<String>();
        List<Int32> _roundRotation = new List<Int32>();

        Int32 _WinTeamId = 0;
        Int32[] indices = null;
        public DataDictionary DataDict = null;
        public DataDictionary RoundDataDict = null;

        public WeaponStatsDictionary W;

        public Dictionary<String, Double> svalue;
        public Dictionary<String, Double> rvalue;

        [A("map")]
        public Int32 CurrentRound { get { return data.CurrentRound; } }
        [A("map")]
        public Int32 TotalRounds { get { return data.TotalRounds; } }
        [A("map")]
        public String MapFileName { get { return (mlist == null) ? data.Map : mlist[MapIndex].MapFileName; } }
        [A("map")]
        public String Gamemode { get { return (mlist == null) ? data.GameMode : mlist[MapIndex].Gamemode; ; } }
        [A("map")]
        public String NextMapFileName { get { return (mlist == null) ? "" : mlist[NextMapIndex].MapFileName; } }
        [A("map")]
        public String NextGamemode { get { return (mlist == null) ? "" : mlist[NextMapIndex].Gamemode; } }
        [A("map")]
        public Int32 MapIndex { get { return indices[0]; } }
        [A("map")]
        public Int32 NextMapIndex { get { return indices[1]; } }
        [A("map")]
        public Double GameModeCounter { get { return plugin.gameModeCounter; } }
        [A("map")]
        public Double CTFRoundTimeModifier { get { return plugin.ctfRoundTimeModifier; } }

        public List<String> MapFileNameRotation { get { return _mapRotation; } }
        public List<String> GamemodeRotation { get { return _modeRotation; } }
        public List<Int32> LevelRoundsRotation { get { return _roundRotation; } }

        [A("round")]
        public Int32 PlayerCount { get { return data.PlayerCount; } }

        [A("round")]
        public Double KillsRound { get { return W.Aggregate("KillsRound"); } }
        [A("round")]
        public Double DeathsRound { get { return W.Aggregate("DeathsRound"); } }
        [A("round")]
        public Double HeadshotsRound { get { return W.Aggregate("HeadshotsRound"); } }
        [A("round")]
        public Double SuicidesRound { get { return W.Aggregate("SuicidesRound"); } }
        [A("round")]
        public Double TeamKillsRound { get { return W.Aggregate("TeamKillsRound"); } }
        [A("round")]
        public Double TimeRound { get { return (Double)data.RoundTime; } }

        [A("total")]
        public Double KillsTotal { get { return W.Aggregate("KillsTotal"); } }
        [A("total")]
        public Double DeathsTotal { get { return W.Aggregate("DeathsTotal"); } }
        [A("total")]
        public Double HeadshotsTotal { get { return W.Aggregate("HeadshotsTotal"); } }
        [A("total")]
        public Double SuicidesTotal { get { return W.Aggregate("SuicidesTotal"); } }
        [A("total")]
        public Double TeamKillsTotal { get { return W.Aggregate("TeamKillsTotal"); } }
        [A("total")]
        public Double TimeTotal { get { return DateTime.Now.Subtract(plugin.enabledTime).TotalSeconds; } }

        [A("total")]
        public Double RoundsTotal { get { return svalue["rounds"]; } internal set { svalue["rounds"] = value; } }

        [A("total")]
        public Double TimeUp { get { return data.ServerUptime; } }

        [A("total")]
        public Int32 MaxPlayers { get { return data.MaxPlayerCount; } }

        public WeaponStatsInterface this[String WeaponName] { get { return W[WeaponName]; } }
        public DataDictionaryInterface Data { get { return (DataDictionaryInterface)DataDict; } }
        public DataDictionaryInterface RoundData { get { return (DataDictionaryInterface)RoundDataDict; } }
        public DataDictionaryInterface DataRound { get { return (DataDictionaryInterface)RoundDataDict; } }

        public Int32 WinTeamId { get { return _WinTeamId; } internal set { _WinTeamId = value; } }

        public Double RemainTickets(Int32 TeamId)
        {
            return Math.Abs(Tickets(TeamId) - TargetTickets);
        }

        public Double RemainTicketsPercent(Int32 TeamId)
        {
            Double stickets = Math.Max(StartTickets(TeamId), TargetTickets);
            Double rtickets = RemainTickets(TeamId);

            if (stickets > 0)
                return Math.Round((rtickets / stickets) * 100.0, 2);

            return Double.NaN;
        }

        public Double StartTickets(Int32 TeamId)
        {
            if (_StartTickets.ContainsKey(TeamId))
                return _StartTickets[TeamId];

            return Double.NaN;
        }

        public Double Tickets(Int32 TeamId)
        {
            Double value = Double.NaN;
            if (data == null)
                return value;

            List<TeamScore> scores = (_TeamTickets == null) ? data.TeamScores : _TeamTickets;
            if (scores == null)
                return value;

            foreach (TeamScore score in scores)
                if (score != null && score.TeamID == TeamId)
                    return (Double)score.Score;

            return value;
        }

        public Double TargetTickets
        {
            get
            {
                //we can take a wild guess for the default, most of the time is 0, except in TDM, and SQDM
                Double value = 0;
                if (data == null)
                    return value;

                List<TeamScore> scores = (_TeamTickets == null) ? data.TeamScores : _TeamTickets;
                if (scores == null)
                    return value;

                // all teams contain the target score (silly PRoCon), first one we find, we return
                // there is a defect in PRoCon prior to 1.1.3.0, where TargetScore was always incorrect
                foreach (TeamScore score in scores)
                    if (score != null)
                        return (Double)score.WinningScore;

                return value;
            }
        }

        public Int32 OppositeTeamId(Int32 TeamId)
        {
            switch (TeamId)
            {
                case 1:
                    return 2;
                case 2:
                    return 1;
                case 3:
                    return 4;
                case 4:
                    return 3;
                default:
                    return 0;
            }
        }

        /* Meta Data */
        public String Port { get { return plugin.server_port; } }
        public String Host { get { return plugin.server_host; } }
        public String Name { get { return plugin.server_name; } }
        public String Description { get { return plugin.server_desc; } }
        public String GameVersion { get { return plugin.game_version; } }

        /* var.* value that is updated every update_interval seconds */
        public Int32 BulletDamage { get { return plugin.varBulletDamage; } }
        public Boolean FriendlyFire { get { return plugin.varFriendlyFire; } }
        public Int32 GunMasterWeaponsPreset { get { return plugin.varGunMasterWeaponsPreset; } }
        public Double IdleTimeout { get { return plugin.varIdleTimeout; } } // seconds
        public Int32 SoldierHealth { get { return plugin.varSoldierHealth; } }
        public Boolean VehicleSpawnAllowed { get { return plugin.varVehicleSpawnAllowed; } }
        public Int32 VehicleSpawnDelay { get { return plugin.varVehicleSpawnDelay; } }
        // BF4
        public Boolean Commander { get { return plugin.varCommander; } }
        public Int32 MaxSpectators { get { return plugin.varMaxSpectators; } }
        public String ServerType { get { return plugin.varServerType; } }
        public Int32 GetFaction(Int32 TeamId)
        {
            if (TeamId < 0 || TeamId >= _Faction.Length)
                return -1;
            return _Faction[TeamId];
        }

        public ServerInfo(InsaneLimits plugin, CServerInfo data, List<MaplistEntry> mlist, Int32[] indices)
        {
            this.plugin = plugin;
            this.data = data;
            this.mlist = mlist;
            this.indices = indices;

            W = new WeaponStatsDictionary(plugin);

            rvalue = new Dictionary<String, Double>();
            svalue = new Dictionary<String, Double>();
            List<String> fields = InsaneLimits.getGameFieldKeys();
            foreach (String field in fields)
            {
                if (!svalue.ContainsKey(field))
                    svalue.Add(field, 0);

                if (!rvalue.ContainsKey(field))
                    rvalue.Add(field, 0);
            }

            DataDict = new DataDictionary(plugin);
            RoundDataDict = new DataDictionary(plugin);
            ResetTickets();

            _Faction = new Int32[5] { -1, -1, -1, -1, -1 };
        }

        private void ResetTickets()
        {
            /* initialize start tickets */
            _StartTickets = new Dictionary<Int32, Double>();
            for (Int32 i = 0; i < 32; i++)
                if (!_StartTickets.ContainsKey(i))
                    _StartTickets.Add(i, Double.NaN);
                else
                    _StartTickets[i] = Double.NaN;
        }

        public void updateField(String name, Double value)
        {
            if (!rvalue.ContainsKey(name))
            {
                plugin.ConsoleError(this.GetType().Name + ", Round-Stats does not contain ^b" + name + "^n");
                return;
            }
            rvalue[name] += value;
        }

        public void AccumulateRoundStats()
        {
            RoundsTotal++;
            W.AccumulateRoundStats();
        }

        public void ResetRoundStats()
        {
            W.ResetRoundStats();
            ResetTickets();
        }

        public void updateRotation(List<MaplistEntry> mlist)
        {
            _mapRotation.Clear();
            _modeRotation.Clear();
            _roundRotation.Clear();

            if (mlist == null) return;

            foreach (MaplistEntry m in mlist)
            {
                _mapRotation.Add(m.MapFileName);
                _modeRotation.Add(m.Gamemode);
                _roundRotation.Add(m.Rounds);
            }
        }

        public void updateMapList(List<MaplistEntry> mlist)
        {
            this.mlist = mlist;
            updateRotation(mlist);
        }

        public void updateIndices(Int32[] indices)
        {
            this.indices = indices;
        }

        public void updateData(CServerInfo data)
        {
            this.data = data;

            /* update the start tickets if needed */
            List<TeamScore> scores = (_TeamTickets == null) ? data.TeamScores : _TeamTickets;
            foreach (TeamScore ts in scores)
                if (ts != null && _StartTickets.ContainsKey(ts.TeamID) && Double.IsNaN(_StartTickets[ts.TeamID]))
                    _StartTickets[ts.TeamID] = ts.Score;
        }

        public void updateTickets(List<TeamScore> tickets)
        {
            this._TeamTickets = tickets;
        }

        public void dumpStatProperties(String scope)
        {
            List<PropertyInfo> plist = plugin.getProperties(this.GetType(), scope);

            Dictionary<String, String> pairs = plugin.buildPairs(this, plist);

            scope = scope.Substring(0, 1).ToUpper() + scope.Substring(1);

            plugin.ConsoleWrite("Server " + scope + "-Stats:");
            plugin.dumpPairs(pairs, 4);
        }

        public void dumpWeaponStats(String scope)
        {

            scope = scope.Substring(0, 1).ToUpper() + scope.Substring(1);

            plugin.ConsoleWrite("Server Weapon " + scope + "-Stats:");
            W.dumpStats(scope, "    ");
        }

    }

    public class A : Attribute
    {
        private String name;
        public String Name { get { return name; } }

        private String scope;
        public String Scope { get { return scope; } }

        private String pattern;
        public String Pattern { get { return pattern; } }

        public A(String scope)
        {
            this.scope = scope;
        }

        public A(String scope, String name, String pattern)
        {
            this.scope = scope;
            this.name = name;
            this.pattern = pattern;
        }
    }

    public class TeamInfo : TeamInfoInterface
    {
        public InsaneLimits plugin = null;
        public Dictionary<String, PlayerInfo> _players = null;
        public ServerInfo server = null;
        public Int32 _TeamId = 0;

        public Int32 TeamId { get { return _TeamId; } }
        public Double ScoreRound { get { return Aggregate("ScoreRound"); } }
        public Double KillsRound { get { return Aggregate("KillsRound"); } }
        public Double DeathsRound { get { return Aggregate("DeathsRound"); } }
        public Double TeamKillsRound { get { return Aggregate("TeamKillsRound"); } }
        public Double TeamDeathsRound { get { return Aggregate("TeamDeathsRound"); } }
        public Double SuicidesRound { get { return Aggregate("SuicidesRound"); } }
        public Double HeadshotsRound { get { return Aggregate("HeadshotsRound"); } }

        public Double TicketsRound { get { return server.Tickets(TeamId); } }
        public Double Tickets { get { return server.Tickets(TeamId); } }
        public Double RemainTickets { get { return server.RemainTickets(TeamId); } }
        public Double RemainTicketsPercent { get { return server.RemainTicketsPercent(TeamId); } }
        public Double StartTickets { get { return server.StartTickets(TeamId); } }
        // BF4
        public Int32 Faction { get { return server.GetFaction(TeamId); } }

        //use a converter to return the list of players as PlayerInfoInterface
        public List<PlayerInfoInterface> players
        {
            get
            {
                return (new List<PlayerInfo>(_players.Values)).ConvertAll<PlayerInfoInterface>
                        (new Converter<PlayerInfo, PlayerInfoInterface>
                            (
                              delegate (PlayerInfo p) { return (PlayerInfoInterface)p; }
                            )
                        );
            }
        }

        public Double Aggregate(String property_name)
        {
            Dictionary<String, Object> dict = new Dictionary<String, Object>();
            foreach (KeyValuePair<String, PlayerInfo> pair in _players)
                dict.Add(pair.Key, (Object)pair.Value);

            return plugin.Aggregate(property_name, typeof(PlayerInfo), dict);
        }

        public TeamInfo(InsaneLimits plugin, Int32 TeamId, Dictionary<String, PlayerInfo> players, ServerInfo server)
        {
            this.plugin = plugin;
            this._TeamId = TeamId;

            _players = new Dictionary<String, PlayerInfo>();
            List<String> keys = new List<String>(players.Keys);
            foreach (String pname in keys)
                if (players.ContainsKey(pname) || players[pname] != null)
                    if (players[pname].TeamId == TeamId)
                        _players.Add(pname, players[pname]);

            this.server = server;

        }
    }

    public class PlayerInfo : PlayerInfoInterface
    {
        public CPlayerInfo info;
        public Dictionary<String, Double> ovalue;
        public Dictionary<String, Double> rvalue;
        public Dictionary<String, Double> svalue;
        public String tag = "";
        public InsaneLimits plugin;
        public CPunkbusterInfo pbInfo;
        public Boolean _stats_error = true;
        public Boolean _battlelog404 = false;
        public DateTime ctime = DateTime.Now;
        public String _last_chat = "";
        public Double _score = 0;
        public WebException _web_exception = null;
        public Int32 _ping = 0;
        public Int32 _maxPing = 0;
        public Int32 _minPing = 0;
        public Int32 _medianPing = 0;
        public Int32 _averagePing = 0;
        public Queue<Int32> _pingQ = new Queue<Int32>();
        public Double _idleTime = 0;

        public WeaponStatsDictionary W = null;
        public BattlelogWeaponStatsDictionary BWS = null;
        public DataDictionary DataDict = null;
        public DataDictionary RoundDataDict = null;

        public Dictionary<String, List<KillInfoInterface>> tkvDict = null;
        public Dictionary<String, List<KillInfoInterface>> tkkDict = null;
        public Dictionary<String, List<KillInfoInterface>> vDict = null;
        public Dictionary<String, List<KillInfoInterface>> kDict = null;

        /* Online statistics (basic)*/
        [A("web", "Rank", @"ra.*")]
        public Double Rank { get { return ovalue["rank"]; } }
        [A("web", "Kdr", @"kd.*")]
        public Double Kdr { get { return ovalue["kdr"]; } }
        [A("web", "Kpm", @"kp.*")]
        public Double Kpm { get { return ratio(Kills, Time / 60.0); } }
        [A("web", "Time", @"ti.*")]
        public Double Time { get { return ovalue["time"]; } }
        [A("web", "Kills", @"ki.*")]
        public Double Kills { get { return ovalue["kills"]; } }
        [A("web", "Wins", @"wi.*")]
        public Double Wins { get { return ovalue["wins"]; } }
        [A("web", "Skill", @"sk.*")]
        public Double Skill { get { return ovalue["skill"]; } }
        [A("web", "Spm", @"sp.*")]
        public Double Spm { get { return ovalue["spm"]; } }
        [A("web", "Score", @"sc.*")]
        public Double Score { get { return ovalue["score"]; } }
        [A("web", "Deaths", @"de.*")]
        public Double Deaths { get { return ovalue["deaths"]; } }
        [A("web", "Losses", @"lo.*")]
        public Double Losses { get { return ovalue["losses"]; } }
        [A("web", "Repairs", @"rep.*")]
        public Double Repairs { get { return ovalue["repairs"]; } }
        [A("web", "Revives", @"rev.*")]
        public Double Revives { get { return ovalue["revives"]; } }
        [A("web", "Accuracy", @"ac.*")]
        public Double Accuracy { get { return ovalue["accuracy"]; } }
        [A("web", "Ressuplies", @"res.*")]
        public Double Ressuplies { get { return ovalue["ressuplies"]; } }
        [A("web", "Quit Percent", @"qu[^ ]*\s*p.*")]
        public Double QuitPercent { get { return ovalue["quit_p"]; } }
        [A("web", "Team Score", @"te[^ ]*\s*sc.*")]
        public Double ScoreTeam { get { return ovalue["sc_team"]; } }
        [A("web", "Combat Score", @"co[^ ]*\s*sc.*")]
        public Double ScoreCombat { get { return ovalue["sc_combat"]; } }
        [A("web", "Vehicle Score", @"ve[^ ]*\s*sc.*")]
        public Double ScoreVehicle { get { return ovalue["sc_vehicle"]; } }
        [A("web", "Objective Score", @"ob[^ ]*\s*sc.*")]
        public Double ScoreObjective { get { return ovalue["sc_objective"]; } }
        [A("web", "Vehicles Killed", @"ve[^ ]*\s*(ki|de).*")]
        public Double VehiclesKilled { get { return ovalue["vehicles_killed"]; } }
        [A("web", "KillStreak Bonus", @"ki[^ ]*\s*(st).*")]
        public Double KillStreakBonus { get { return ovalue["killStreakBonus"]; } }

        [A("web", "Kill Assists", @"killAssists")]
        public Double KillAssists { get { return ovalue["killAssists"]; } }
        [A("web", "rsDeaths", @"rsDeaths")]
        public Double ResetDeaths { get { return ovalue["rsDeaths"]; } }
        [A("web", "rsKills", @"rsKills")]
        public Double ResetKills { get { return ovalue["rsKills"]; } }
        [A("web", "rsNumLosses", @"rsNumLosses")]
        public Double ResetLosses { get { return ovalue["rsNumLosses"]; } }
        [A("web", "rsNumWins", @"rsNumWins")]
        public Double ResetWins { get { return ovalue["rsNumWins"]; } }
        [A("web", "rsScore", @"rsScore")]
        public Double ResetScore { get { return ovalue["rsScore"]; } }
        [A("web", "rsShotsFired", @"rsShotsFired")]
        public Double ResetShotsFired { get { return ovalue["rsShotsFired"]; } }
        [A("web", "rsShotsHit", @"rsShotsHit")]
        public Double ResetShotsHit { get { return ovalue["rsShotsHit"]; } }
        [A("web", "rsTimePlayed", @"rsTimePlayed")]
        public Double ResetTime { get { return ovalue["rsTimePlayed"]; } }

        /* Online statistics (extra) */
        [A("web", "Recon Time", @"re[^ ]*\s*ti.*")]
        public Double ReconTime { get { return ovalue["recon_t"]; } }
        [A("web", "Engineer Time", @"en[^ ]*\s*ti.*")]
        public Double EngineerTime { get { return ovalue["engineer_t"]; } }
        [A("web", "Assault Time", @"as[^ ]*\s*ti.*")]
        public Double AssaultTime { get { return ovalue["assault_t"]; } }
        [A("web", "Support Time", @"su[^ ]*\s*ti.*")]
        public Double SupportTime { get { return ovalue["support_t"]; } }
        [A("web", "Vehicle Time", @"ve[^ ]*\s*ti.*")]
        public Double VehicleTime { get { return ovalue["vehicle_t"]; } }
        [A("web", "Recon Percent", @"re[^ ]*\s*(pe|%).*")]
        public Double ReconPercent { get { return ovalue["recon_p"]; } }
        [A("web", "Engineer Percent", @"en[^ ]*\s*(pe|%).*")]
        public Double EngineerPercent { get { return ovalue["engineer_p"]; } }
        [A("web", "Assault Percent", @"as[^ ]*\s*(pe|%).*")]
        public Double AssaultPercent { get { return ovalue["assault_p"]; } }
        [A("web", "Support Percent", @"su[^ ]*\s*(pe|%).*")]
        public Double SupportPercent { get { return ovalue["support_p"]; } }
        [A("web", "Vehicle Percent", @"ve[^ ]*\s*(pe|%).*")]
        public Double VehiclePercent { get { return ovalue["vehicle_p"]; } }

        /* Player data */

        public String Name { get { return pbInfo.SoldierName; } }
        public String FullName { get { return (Tag.Length > 0) ? "[" + Tag + "]" + Name : Name; } }
        public String FullDisplayName { get { return (Tag.Length > 0) ? "^b[^n" + Tag + "^b]^n^b" + Name + "^n" : "^b" + Name + "^n"; } }
        public String Tag { get { return tag; } }
        public String EAGuid { get { return info.GUID; } }
        public String IPAddress { get { return pbInfo.Ip; } }
        public String CountryCode { get { return pbInfo.PlayerCountryCode; } }
        public String CountryName { get { return pbInfo.PlayerCountry; } }
        public String PBGuid { get { return pbInfo.GUID; } }
        public Int32 TeamId { get { return info.TeamID; } set { info.TeamID = value; } }
        public Int32 SquadId { get { return info.SquadID; } set { info.SquadID = value; } }
        public Int32 Ping { get { return _ping; } set { _ping = value; } }
        public Int32 MaxPing { get { return _maxPing; } set { _maxPing = value; } }
        public Int32 MinPing { get { return _minPing; } set { _minPing = value; } }
        public Int32 MedianPing { get { return _medianPing; } set { _medianPing = value; } }
        public Int32 AveragePing { get { return _averagePing; } set { _averagePing = value; } }
        public Int32 Role { get { return info.Type; } }

        /* Round Statistics */
        [A("round", "Kdr", @"kd.*")]
        public Double KdrRound { get { return ratio(KillsRound, DeathsRound); } }
        [A("round", "Kpm", @"kp.*")]
        public Double KpmRound { get { return ratio(KillsRound, TimeRound / 60.0); } }
        [A("round", "Spm", @"sp.*")]
        public Double SpmRound { get { return ratio(ScoreRound, TimeRound / 60.0); } }
        [A("round", "Score", @"sc.*")]
        public Double ScoreRound { get { return _score; } set { _score = value; } }
        [A("round", "Kills", @"ki.*")]
        public Double KillsRound { get { return W.Aggregate("KillsRound"); } }
        [A("round", "Deaths", @"de.*")]
        public Double DeathsRound { get { return W.Aggregate("DeathsRound"); } }
        [A("round", "Headshots", @"h(e|s).*")]
        public Double HeadshotsRound { get { return W.Aggregate("HeadshotsRound"); } }
        [A("round", "Team Kills", @"te[^ ]*\s*ki.*")]
        public Double TeamKillsRound { get { return W.Aggregate("TeamKillsRound"); } }
        [A("round", "Team Deaths", @"te[^ ]*\s*de.*")]
        public Double TeamDeathsRound { get { return W.Aggregate("TeamDeathsRound"); } }
        [A("round", "Suicides", @"su.*")]
        public Double SuicidesRound { get { return W.Aggregate("SuicidesRound"); } }
        [A("round", "Time", @"ti.*")]
        public Double TimeRound { get { return Math.Min(TimeTotal, ((plugin.serverInfo == null) ? TimeTotal : plugin.serverInfo.TimeRound)); } }

        /* Total In-Server Stats */
        [A("total", "Kdr", @"kd.*")]
        public Double KdrTotal { get { return ratio(KillsTotal, DeathsTotal); } }
        [A("total", "Kpm", @"kp.*")]
        public Double KpmTotal { get { return ratio(KillsTotal, TimeTotal / 60.0); } }
        [A("total", "Spm", @"sp.*")]
        public Double SpmTotal { get { return ratio(ScoreTotal, TimeTotal / 60.0); } }
        [A("total", "Score", @"sc.*")]
        public Double ScoreTotal { get { return svalue["score"] + ScoreRound; } internal set { svalue["score"] = value; } }
        [A("total", "Kills", @"ki.*")]
        public Double KillsTotal { get { return W.Aggregate("KillsTotal"); } }
        [A("total", "Deaths", @"de.*")]
        public Double DeathsTotal { get { return W.Aggregate("DeathsTotal"); } }
        [A("total", "Headshots", @"h(e|s).*")]
        public Double HeadshotsTotal { get { return W.Aggregate("HeadshotsTotal"); } }
        [A("total", "Team Kills", @"te[^ ]*\s*ki.*")]
        public Double TeamKillsTotal { get { return W.Aggregate("TeamKillsTotal"); } }
        [A("total", "Team Deaths", @"te[^ ]*\s*de.*")]
        public Double TeamDeathsTotal { get { return W.Aggregate("TeamDeathsTotal"); } }
        [A("total", "Suicides", @"su.*")]
        public Double SuicidesTotal { get { return W.Aggregate("SuicidesTotal"); } }
        [A("total", "Time", "ti.*")]
        public Double TimeTotal { get { return DateTime.Now.Subtract(JoinTime).TotalSeconds; } }
        [A("total", "Rounds", @"ro.*")]
        public Double RoundsTotal { get { return svalue["rounds"]; } internal set { svalue["rounds"] = value; } }

        public Dictionary<String, List<KillInfoInterface>> TeamKillVictims { get { return tkvDict; } }
        public Dictionary<String, List<KillInfoInterface>> TeamKillKillers { get { return tkkDict; } }
        public Dictionary<String, List<KillInfoInterface>> Victims { get { return vDict; } }
        public Dictionary<String, List<KillInfoInterface>> Killers { get { return kDict; } }

        /* Other data */

        public DateTime JoinTime { get { return ctime; } }
        public String LastChat { get { return _last_chat; } set { _last_chat = value; } }
        public Boolean Battlelog404 { get { return _battlelog404; } set { _battlelog404 = value; } }
        public Boolean StatsError { get { return _stats_error; } set { _stats_error = value; } }

        /* Whitelist info */
        public Boolean inClanWhitelist { get { return plugin.isInClanWhitelist(Name); } }
        public Boolean inPlayerWhitelist { get { return plugin.isInPlayerWhitelist(Name); } }
        public Boolean isInWhitelist { get { return plugin.isInWhitelist(Name); } }

        public WeaponStatsInterface this[String WeaponName] { get { return W[WeaponName]; } }
        public BattlelogWeaponStatsInterface GetBattlelog(String WeaponName) { return BWS[WeaponName]; }
        public DataDictionaryInterface Data { get { return (DataDictionaryInterface)DataDict; } }
        public DataDictionaryInterface RoundData { get { return (DataDictionaryInterface)RoundDataDict; } }
        public DataDictionaryInterface DataRound { get { return (DataDictionaryInterface)RoundDataDict; } }

        private void setField(String name, Double value)
        {
            if (!rvalue.ContainsKey(name))
            {
                plugin.ConsoleError(this.GetType().Name + " Round-Stats does not contain ^b" + name + "^n");
                return;
            }

            Double diff = value - rvalue[name];
            rvalue[name] = value;

            if (plugin.serverInfo == null || diff <= 0)
                return;

            plugin.serverInfo.updateField(name, diff);

        }

        public void updateInfo(CPlayerInfo info)
        {
            //hack, so that we can count score from 0
            if (Double.IsNaN(ScoreRound))
            {
                this.info.Score = info.Score;
                ScoreRound = 0;
            }

            Int32 new_score = info.Score;
            Int32 old_score = this.info.Score;
            Int32 score_change = (new_score - old_score);

            this.info = info;

            ScoreRound += score_change;
        }

        public void AccumulateRoundStats()
        {
            // I know what you are thinking, WTF ... (take a look at the set/get methods)
            ScoreTotal = ScoreTotal;
            RoundsTotal++;
            W.AccumulateRoundStats();
        }

        public void ResetRoundStats()
        {
            ScoreRound = 0;
            this.info.Score = 0;
            W.ResetRoundStats();
        }

        public PlayerInfo(InsaneLimits plugin, CPunkbusterInfo pbInfo)
        {
            this.pbInfo = pbInfo;
            this.plugin = plugin;
            this.info = new CPlayerInfo(pbInfo.SoldierName, "", 0, 0);

            ovalue = new Dictionary<String, Double>();
            svalue = new Dictionary<String, Double>();
            rvalue = new Dictionary<String, Double>();

            // fields for web stats
            List<String> fields = InsaneLimits.getBasicFieldKeys(plugin.game_version);
            fields.AddRange(InsaneLimits.getExtraFields());
            foreach (String field_name in fields)
                ovalue.Add(field_name, /* Double.NaN */ 0);

            // fields for game stats
            List<String> gfields = InsaneLimits.getGameFieldKeys();
            foreach (String field_name in gfields)
            {
                svalue.Add(field_name, 0);
                rvalue.Add(field_name, 0);
            }

            W = new WeaponStatsDictionary(plugin);
            BWS = new BattlelogWeaponStatsDictionary(plugin);
            ScoreRound = Double.NaN;

            DataDict = new DataDictionary(plugin);
            RoundDataDict = new DataDictionary(plugin);

            tkvDict = new Dictionary<String, List<KillInfoInterface>>();
            tkkDict = new Dictionary<String, List<KillInfoInterface>>();
            vDict = new Dictionary<String, List<KillInfoInterface>>();
            kDict = new Dictionary<String, List<KillInfoInterface>>();
        }

        public void teamKilled(PlayerInfo victim, Kill kinfo)
        {

        }

        public Double ratio(Double left, Double right)
        {
            if (Double.IsNaN(left) || Double.IsNaN(right) || left <= 0)
                return 0;
            if (right <= 0)
                right = 1;

            return left / right;

        }

        public override String ToString()
        {
            List<String> values = new List<String>();
            foreach (String key in ovalue.Keys)
                values.Add(key + "(" + Math.Round(ovalue[key], 2) + ")");

            return "tag(" + tag + ")," + String.Join(", ", values.ToArray());
        }

        public void dumpStatProperties(String scope)
        {
            dumpStatProperties(scope, null);
        }

        public void dumpStatProperties(String scope, String logName)
        {
            List<PropertyInfo> plist = plugin.getProperties(this.GetType(), scope);

            Dictionary<String, String> pairs = plugin.buildPairs(this, plist);

            scope = scope.Substring(0, 1).ToUpper() + scope.Substring(1);

            String log = (logName == null) ? "plugin.log" : logName;

            plugin.DebugWrite(scope + "-Stats for " + FullDisplayName + " logged to: " + log, 3);
            plugin.dumpPairs(pairs, 4, logName);
        }

        public void dumpWeaponStats(String scope)
        {
            scope = scope.Substring(0, 1).ToUpper() + scope.Substring(1);
            plugin.ConsoleWrite("Weapon " + scope + "-Stats for " + FullDisplayName + ":");
            W.dumpStats(scope, "    ");
        }
    }

    public class DataDictionary : DataDictionaryInterface
    {
        InsaneLimits plugin = null;

        public Dictionary<Type, Dictionary<String, Object>> data = new Dictionary<Type, Dictionary<String, Object>>();

        public DataDictionary(InsaneLimits plugin)
        {
            this.plugin = plugin;

            Init();
        }

        public void Init()
        {
            List<Type> types = new List<Type>(new Type[] { typeof(String), typeof(Int32), typeof(Double), typeof(Boolean), typeof(Object) });

            foreach (Type type in types)
                if (!data.ContainsKey(type))
                    data.Add(type, new Dictionary<String, Object>());
        }

        /* Generic set/get/unset/isset methods */

        public Object set(Type type, String key, Object value)
        {
            lock (data)
            {
                if (data.ContainsKey(type))
                {
                    if (!data[type].ContainsKey(key))
                        data[type].Add(key, value);
                    else
                        data[type][key] = value;

                    return data[type][key];
                }
            }
            plugin.ConsoleError(this.GetType().Name + " has no data of ^b" + type.Name + "^n type");
            return (Object)Activator.CreateInstance(type);
        }

        public Object get(Type type, String key)
        {
            Boolean unknownKey = false;
            lock (data)
            {
                if (data.ContainsKey(type))
                {
                    if (!data[type].ContainsKey(key))
                    {
                        unknownKey = true;
                    }
                    else
                    {
                        return data[type][key];
                    }
                }
            }
            if (unknownKey)
            {
                plugin.ConsoleError(this.GetType().Name + " has no ^b" + type.Name + "^n(" + key + ") key");
                return (Object)Activator.CreateInstance(type);
            }
            plugin.ConsoleError(this.GetType().Name + " has no data of ^b" + type.Name + "^n type");
            return (Object)Activator.CreateInstance(type);
        }

        public Object unset(Type type, String key)
        {
            Boolean unknownKey = false;
            lock (data)
            {
                if (data.ContainsKey(type))
                {
                    if (!data[type].ContainsKey(key))
                    {
                        unknownKey = true;
                    }
                    else
                    {
                        Object value = data[type][key];
                        data[type].Remove(key);

                        return value;
                    }
                }
            }
            if (unknownKey)
            {
                plugin.ConsoleWarn(this.GetType().Name + " has no ^b" + type.Name + "^n(" + key + ") key");
                return (Object)Activator.CreateInstance(type);
            }
            plugin.ConsoleError(this.GetType().Name + " has no data of ^b" + type.Name + "^n type");
            return (Object)Activator.CreateInstance(type);
        }

        public List<String> getKeys(Type type)
        {
            lock (data)
            {
                if (data.ContainsKey(type))
                {
                    return new List<String>(data[type].Keys);
                }
            }
            plugin.ConsoleError(this.GetType().Name + " has no data of ^b" + type.Name + "^n type");
            return new List<String>();
        }

        public void Clear()
        {
            lock (data)
            {
                data.Clear();
                Init();
            }
        }

        public Boolean isset(Type type, String key)
        {
            lock (data)
            {
                if (data.ContainsKey(type))
                {
                    return data[type].ContainsKey(key);
                }
            }
            plugin.ConsoleError(this.GetType().Name + " has no data of ^b" + type.Name + "^n type");
            return false;
        }

        /* String Data */
        public String setString(String key, String value)
        {
            return (String)set(typeof(String), key, (Object)value);
        }

        public String getString(String key)
        {
            return (String)get(typeof(String), key);
        }

        public Boolean issetString(String key)
        {
            return isset(typeof(String), key);
        }

        public String unsetString(String key)
        {
            return (String)unset(typeof(String), key);
        }

        public List<String> getStringKeys()
        {
            return getKeys(typeof(String));
        }

        /* Int Data */
        public Int32 setInt(String key, Int32 value)
        {
            return (Int32)set(typeof(Int32), key, (Object)value);
        }

        public Int32 getInt(String key)
        {
            return (Int32)get(typeof(Int32), key);
        }

        public Boolean issetInt(String key)
        {
            return isset(typeof(Int32), key);
        }

        public Int32 unsetInt(String key)
        {
            return (Int32)unset(typeof(Int32), key);
        }

        public List<String> getIntKeys()
        {
            return getKeys(typeof(Int32));
        }

        /* Double Data */
        public Double setDouble(String key, Double value)
        {
            return (Double)set(typeof(Double), key, (Object)value);
        }

        public Double getDouble(String key)
        {
            return (Double)get(typeof(Double), key);
        }

        public Boolean issetDouble(String key)
        {
            return isset(typeof(Double), key);
        }

        public Double unsetDouble(String key)
        {
            return (Double)unset(typeof(Double), key);
        }

        public List<String> getDoubleKeys()
        {
            return getKeys(typeof(Double));
        }

        /* Bool Data */
        public Boolean setBool(String key, Boolean value)
        {
            return (Boolean)set(typeof(Boolean), key, (Object)value);
        }

        public Boolean getBool(String key)
        {
            return (Boolean)get(typeof(Boolean), key);
        }

        public Boolean issetBool(String key)
        {
            return isset(typeof(Boolean), key);
        }

        public Boolean unsetBool(String key)
        {
            return (Boolean)unset(typeof(Boolean), key);
        }

        public List<String> getBoolKeys()
        {
            return getKeys(typeof(Boolean));
        }

        /* Object Data */
        public Object setObject(String key, Object value)
        {
            return (Object)set(typeof(Object), key, (Object)value);
        }

        public Object getObject(String key)
        {
            return (Object)get(typeof(Object), key);
        }

        public Boolean issetObject(String key)
        {
            return isset(typeof(Object), key);
        }

        public Object unsetObject(String key)
        {
            return (Object)unset(typeof(Object), key);
        }

        public List<String> getObjectKeys()
        {
            return getKeys(typeof(Object));
        }
    }

    public class BattlelogWeaponStats : BattlelogWeaponStatsInterface
    {
        Double _kills = 0;
        Double _shots_hit = 0;
        Double _shots_fired = 0;
        Double _time_equipped = 0;
        Double _headshots = 0;
        String _category = String.Empty;
        String _name = String.Empty;
        String _slug = String.Empty;
        String _code = String.Empty;

        public String Category { get { return _category; } set { _category = value; } }
        public String Name { get { return _name; } set { _name = value; } }
        public String Slug { get { return _slug; } set { _slug = value; } }
        public String Code { get { return _code; } set { _code = value; } }

        public Double Kills { get { return _kills; } set { _kills = value; } }
        public Double ShotsFired { get { return _shots_fired; } set { _shots_fired = value; } }
        public Double ShotsHit { get { return _shots_hit; } set { _shots_hit = value; } }
        public Double Accuracy { get { return (ShotsFired > 0 && ShotsHit > 0) ? ((ShotsHit / ShotsFired) * 100) : 0; } }
        public Double Headshots { get { return _headshots; } set { _headshots = value; } }
        public Double TimeEquipped { get { return _time_equipped; } set { _time_equipped = value; } }

    }

    public class WeaponStats : WeaponStatsInterface
    {
        Double _kills = 0;
        Double _kills_total = 0;

        Double _deaths = 0;
        Double _deaths_total = 0;

        Double _suicides = 0;
        Double _suicides_total = 0;

        Double _teamkills = 0;
        Double _teamkills_total = 0;

        Double _teamdeaths = 0;
        Double _teamdeaths_total = 0;

        Double _headshots = 0;
        Double _headshots_total = 0;

        [A("round")]
        public Double KillsRound { get { return _kills; } internal set { _kills = value; } }
        [A("round")]
        public Double DeathsRound { get { return _deaths; } internal set { _deaths = value; } }
        [A("round")]
        public Double SuicidesRound { get { return _suicides; } internal set { _suicides = value; } }
        [A("round")]
        public Double TeamKillsRound { get { return _teamkills; } internal set { _teamkills = value; } }
        [A("round")]
        public Double TeamDeathsRound { get { return _teamdeaths; } internal set { _teamdeaths = value; } }
        [A("round")]
        public Double HeadshotsRound { get { return _headshots; } internal set { _headshots = value; } }

        [A("total")]
        public Double KillsTotal { get { return _kills_total + KillsRound; } internal set { _kills_total = value; } }
        [A("total")]
        public Double DeathsTotal { get { return _deaths_total + DeathsRound; } internal set { _deaths_total = value; } }
        [A("total")]
        public Double SuicidesTotal { get { return _suicides_total + SuicidesRound; } internal set { _suicides_total = value; } }
        [A("total")]
        public Double TeamKillsTotal { get { return _teamkills_total + TeamKillsRound; } internal set { _teamkills_total = value; } }
        [A("total")]
        public Double TeamDeathsTotal { get { return _teamdeaths_total + TeamDeathsRound; } internal set { _teamdeaths_total = value; } }
        [A("total")]
        public Double HeadshotsTotal { get { return _headshots_total + HeadshotsRound; } internal set { _headshots_total = value; } }

        public void ResetRoundStats()
        {
            KillsRound = 0;
            DeathsRound = 0;
            SuicidesRound = 0;
            HeadshotsRound = 0;
            TeamKillsRound = 0;
            TeamDeathsRound = 0;
        }

        public void AccumulateRoundStats()
        {
            // I know you are thinking, WTF ... just look at the set/get
            KillsTotal = KillsTotal;
            DeathsTotal = DeathsTotal;
            SuicidesTotal = SuicidesTotal;
            HeadshotsTotal = HeadshotsTotal;
            TeamKillsTotal = TeamKillsTotal;
            TeamDeathsTotal = TeamDeathsTotal;
        }
    }

    public class WeaponStatsDictionary
    {
        InsaneLimits plugin = null;
        public Dictionary<String, WeaponStats> data;
        public WeaponStatsDictionary parent = null;
        WeaponStats NullWeaponStats = new WeaponStats();

        private void init(InsaneLimits plugin)
        {
            this.plugin = plugin;
            data = new Dictionary<String, WeaponStats>();
        }

        public WeaponStatsDictionary(InsaneLimits plugin)
        {
            init(plugin);
        }

        public WeaponStatsDictionary(InsaneLimits plugin, WeaponStatsDictionary parent)
        {
            init(plugin);
            this.parent = parent;
        }

        public WeaponStats this[String WeaponName] { get { return getWeaponData(WeaponName); } }

        private String bestWeaponMatch(String name)
        {
            return bestWeaponMatch(name, true);
        }

        private String bestWeaponMatch(String name, Boolean verbose)
        {

            Boolean EventWeapon = false;
            if (name.StartsWith(":") && (name = name.Substring(1)).Length > 0)
                EventWeapon = true;
            else if (name.StartsWith("U_")) // BF4
                EventWeapon = true;

            if (plugin.WeaponsDict.ContainsKey(name))
                return name;
            else if (EventWeapon)
            {
                plugin.DebugWrite("detected that weapon ^b" + name + "^n is not in dictionary, adding it", 4);
                try { plugin.WeaponsDict.Add(name, DamageTypes.None); }
                catch (Exception) { }
                return name;
            }

            Int32 distance = 0;
            List<String> names = new List<String>(plugin.WeaponsDict.Keys);
            String new_name = plugin.bestMatch(name, names, out distance, false);
            if (new_name == null)
            {
                if (verbose)
                    plugin.ConsoleError("could not find weapon ^b" + name + "^n in dictionary");
                return null;
            }

            if (verbose)
                plugin.DebugWrite("could not find weapon ^b" + name + "^n, but found ^b" + new_name + "^n, edit distance of ^b" + distance + "^n", 4);
            return new_name;
        }

        public WeaponStats getWeaponData(String name)
        {

            try
            {
                // special case
                if (name.Equals("UnkownWeapon"))
                    return NullWeaponStats;

                // the easy case first, weapon is in dictionary
                name = bestWeaponMatch(name);

                if (name == null)
                    return NullWeaponStats;

                if (!data.ContainsKey(name))
                    data.Add(name, new WeaponStats());

                return data[name];
            }
            catch (Exception e)
            {
                plugin.DumpException(e);
            }

            return NullWeaponStats;
        }

        public void ResetRoundStats()
        {
            foreach (KeyValuePair<String, WeaponStats> pair in data)
                pair.Value.ResetRoundStats();

        }

        public void AccumulateRoundStats()
        {
            foreach (KeyValuePair<String, WeaponStats> pair in data)
                pair.Value.AccumulateRoundStats();
        }

        public Double Aggregate(String property_name)
        {
            Dictionary<String, Object> dict = new Dictionary<String, Object>();
            foreach (KeyValuePair<String, WeaponStats> pair in data)
                dict.Add(pair.Key, (Object)pair.Value);

            return plugin.Aggregate(property_name, typeof(WeaponStats), dict);
        }

        /*
        public Double Aggregate(String property_name)
        {

            Double total = 0;
            Type type = typeof(WeaponStats);
            PropertyInfo property = type.GetProperty(property_name);

            if (property == null)
            {
                plugin.ConsoleError(type.Name + ".^b" + property_name + "^n does not exist");
                return 0;
            }

            foreach (KeyValuePair<String, WeaponStats> pair in data)
            {
                if (pair.Value == null)
                    continue;

                Double value = 0;
                // I know this is awefully slow, but better be safe for future changes
                if (!Double.TryParse(property.GetValue(pair.Value, null).ToString(), out value))
                {
                    plugin.ConsoleError(type.Name + "." + property.Name + ", cannot be cast to ^b" + typeof(Double).Name + "^n");
                    return 0;
                }
                total += value;
            }

            return total;
        }
        */

        public void dumpStats(String source, String prefix)
        {
            Type type = typeof(WeaponStats);
            List<PropertyInfo> properties = new List<PropertyInfo>(type.GetProperties());

            //remove the properties not matching source

            properties.RemoveAll(delegate (PropertyInfo property)
            {
                Object[] attrs = property.GetCustomAttributes(true);
                return (attrs.Length == 0 || !typeof(A).Equals(attrs[0].GetType()) || !((A)attrs[0]).Scope.ToLower().Equals(source.ToLower()));
            });

            foreach (KeyValuePair<String, WeaponStats> pair in data)
            {
                WeaponStats wstats = pair.Value;
                String weapon_name = pair.Key;

                List<String> properties_data = new List<String>();

                for (Int32 i = 0; i < properties.Count; i++)
                {
                    PropertyInfo property = properties[i];
                    Double value = 0;
                    if (!Double.TryParse(property.GetValue(wstats, null).ToString(), out value))
                        value = Double.NaN;

                    if (Double.IsNaN(value))
                    {
                        plugin.ConsoleError(type.Name + "." + property.Name + ", is not of " + typeof(Double).Name + " type");
                        continue;
                    }
                    value = Math.Round(value, 2);

                    if (value == 0)
                        continue;

                    properties_data.Add(property.Name + "(" + value + ")");
                }

                if (properties_data.Count == 0)
                    continue;

                plugin.ConsoleWrite(prefix + weapon_name + " ^b=^n " + String.Join(", ", properties_data.ToArray()));
            }
        }
    }

    public class BattlelogWeaponStatsDictionary
    {
        InsaneLimits plugin = null;
        public Dictionary<String, BattlelogWeaponStats> data;
        BattlelogWeaponStats NullWeaponStats = new BattlelogWeaponStats();
        BattlelogWeaponStats UnknownWeaponStats = new BattlelogWeaponStats();
        BattlelogWeaponStats SkippedWeaponStats = new BattlelogWeaponStats();

        private void init(InsaneLimits plugin)
        {
            this.plugin = plugin;
            data = new Dictionary<String, BattlelogWeaponStats>();
            UnknownWeaponStats.Name = "UNKNOWN";
            UnknownWeaponStats.Kills = -1;
            UnknownWeaponStats.ShotsFired = -1;
            UnknownWeaponStats.Headshots = -1;
            SkippedWeaponStats.Name = "UNAVAILABLE";
            SkippedWeaponStats.Category = SkippedWeaponStats.Name;
            SkippedWeaponStats.Slug = SkippedWeaponStats.Name;
            SkippedWeaponStats.Code = SkippedWeaponStats.Name;
        }

        public BattlelogWeaponStatsDictionary(InsaneLimits plugin)
        {
            init(plugin);
        }

        public BattlelogWeaponStats this[String WeaponName] { get { return getWeaponData(WeaponName); } }

        private String bestWeaponMatch(String name)
        {
            return bestWeaponMatch(name, true);
        }

        private String bestWeaponMatch(String name, Boolean verbose)
        {
            /* Example mappings. RCON weapon name in [brackets].
            [Siaga20k]
            Category:Shotguns, Name:Saiga, Slug:saiga-12k, Code:sgSaiga, Kills:4, ShotsFired:93, ShotsHit:44, Accuracy:47.31, Headshots:2, TimeEquipped:00:05:41
            
            [Weapons/MP443/MP443_GM]
            Category:Handheld weapons, Name:MP443 LIT, Slug:mp443-tact, Code:pMP443L, Kills:0, ShotsFired:8, ShotsHit:0, Accuracy:0.00, Headshots:0, TimeEquipped:00:00:25
            
            [?]
            Category:Handheld weapons, Name:MP443 Silenced, Slug:mp443-supp, Code:pMP443S, Kills:0, ShotsFired:0, ShotsHit:0, Accuracy:0.00, Headshots:0, TimeEquipped:00:00:00
            
            [Weapons/MP443/MP443]
            Category:Handheld weapons, Name:MP 443, Slug:mp443, Code:pMP443, Kills:18, ShotsFired:782, ShotsHit:95, Accuracy:12.15, Headshots:4, TimeEquipped:00:47:30
            
            [SCARL]
            Category:Assault rifles, Name:XP2 SCARL, Slug:scar-l, Code:arSCARL, Kills:89, ShotsFired:4431, ShotsHit:567, Accuracy:12.80, Headshots:11, TimeEquipped:01:22:29
            
            [FGM-148]
            Category:Launchers, Name:FGM-148 JAVELIN, Slug:fgm-148-javelin, Code:wLATJAV, Kills:8, ShotsFired:115, ShotsHit:68, Accuracy:59.13, Headshots:0, TimeEquipped:00:36:07
            
            BF4 Example
            [U_FY-JS]
            Category:Sniper Rifles, Name:WARSAW_ID_P_WNAME_FYJS, Slug:fy-js, Code:wSR, Kills:385, ShortsFired:14659, ShotsHit:2192, Accuracy:0.15, Headshots:74, TimeEquipped:04:55:23

            */

            String shortName = name; // RCON name
            Match m = Regex.Match(name, @"/([^/]+)$");
            if (m.Success) shortName = m.Groups[1].Value;
            String bf4Normalized = name;

            // User mapping
            if (plugin.rcon2bw_user.ContainsKey(name)) return plugin.rcon2bw_user[shortName];

            if (plugin.game_version == "BF4" || plugin.game_version == "BFHL")
            {
                KillReasonInterface kr = plugin.FriendlyWeaponName(bf4Normalized);
                shortName = kr.Name.ToUpper();
                bf4Normalized = "WARSAW_ID_P_WNAME_" + shortName;
                plugin.DebugWrite("^9bestWeaponMatch(" + name + "), BF4 normalized = " + bf4Normalized, 8);
            }

            // Exact match?
            if (plugin.game_version == "BF4" || plugin.game_version == "BFHL")
            {
                if (data.ContainsKey(bf4Normalized)) return bf4Normalized;
                // Try alternative name
                bf4Normalized = "WARSAW_ID_I_NAME_" + shortName;
                if (data.ContainsKey(bf4Normalized)) return bf4Normalized;
            }
            else
            {
                if (data.ContainsKey(name)) return name;
                if (data.ContainsKey(shortName)) return shortName;
            }

            // Special cases
            if (plugin.game_version == "BF4" || plugin.game_version == "BFHL")
            {
                if (plugin.rcon2bwbf4.ContainsKey(shortName)) return plugin.rcon2bwbf4[shortName];
            }
            else
            {
                if (plugin.rcon2bw.ContainsKey(shortName)) return plugin.rcon2bw[shortName];
            }

            if (data.Keys.Count == 0)
            {
                if (verbose) plugin.DebugWrite("^1^bWARNING^0^n: GetBattlelog: use_slow_weapon_stats was False at time of fetch for this player, so no stats to find for " + shortName, 4);
                return shortName;
            }

            // Narrow down keys to those that contain shortName as a substring
            List<String> keys = new List<String>(data.Keys);

            List<String> subKeys = new List<String>();
            foreach (String k in keys)
            {
                if (Regex.Match(k, shortName, RegexOptions.IgnoreCase).Success) subKeys.Add(k);
            }

            // Exactly one key contains shortName as a substring?
            if (subKeys.Count == 1) return subKeys[0];

            // If no substrings match exactly, use the entire list of keys
            if (subKeys.Count == 0) subKeys = keys;

            // Last resort, do fuzzy match
            Int32 distance = 0;
            String new_name = plugin.bestMatch((plugin.game_version == "BF4" || plugin.game_version == "BFHL") ? bf4Normalized : shortName, subKeys, out distance, true);
            if (new_name == null)
            {
                if (verbose)
                    plugin.DebugWrite("^1^bWARNING^0^n: GetBattlelog: could not find weapon ^b" + shortName + "^n in dictionary", 4);
                return null;
            }

            if (verbose)
                plugin.DebugWrite("^1^bWARNING^0^n: GetBattlelog: could not find weapon ^b" + shortName + "^n, but guessed ^b" + new_name + "^n, with inaccuracy of ^b" + distance.ToString("F3") + "^n (10 or less is a good guess)", 4);
            return new_name;
        }

        public BattlelogWeaponStats getWeaponData(String name)
        {
            if (data.Keys.Count == 0)
            {
                plugin.DebugWrite("^1^bWARNING^0^n: GetBattlelog: use_slow_weapon_stats was False at time of fetch for this player, so no weapon stats available", 4);
                return SkippedWeaponStats;
            }

            try
            {
                // special case
                if (name.Equals("UnknownWeapon")) return UnknownWeaponStats;

                // the easy case first, weapon is in dictionary
                name = bestWeaponMatch(name);

                if (name == null) return UnknownWeaponStats;

                if (!data.ContainsKey(name))
                {
                    data.Add(name, new BattlelogWeaponStats());
                    plugin.DebugWrite("^9getWeaponData(" + name + "): unknown, added default BattlelogWeaponStats", 8);
                }

                return data[name];
            }
            catch (Exception e)
            {
                plugin.DumpException(e);
            }

            return NullWeaponStats;
        }

        public void setWeaponData(List<BattlelogWeaponStats> bws)
        {
            if (bws == null) return;

            foreach (BattlelogWeaponStats s in bws)
            {
                String key = s.Name;
                if (plugin.game_version == "BF3")
                {
                    /*
                    The names used in the stats are different from the RCON weapon names!
                    Usually the Slug name is the closest, but sometimes the
                    Name is a closer match to the RCON weapon name. As a rough
                    heuristic, we select the shortest String between Name and Slug,
                    favoring Name if they are equal in length.
                    */
                    if (s.Slug.Length < s.Name.Length) key = s.Slug;
                }

                data[key] = s;
            }
        }

        public void dumpMatchedStats()
        {
            List<String> rconNames = new List<String>(plugin.WeaponsDict.Keys);

            foreach (String rconName in rconNames)
            {
                String shortName = rconName;
                Match m = Regex.Match(rconName, @"/([^/]+)$");
                if (m.Success) shortName = m.Groups[1].Value;

                BattlelogWeaponStats bws = this[rconName];

                plugin.ConsoleWrite("(" + shortName + ") = Name:" + bws.Name + ", Slug:" + bws.Slug + ", C:" + bws.Code + ", Kills:" + bws.Kills.ToString("F0") + ", Fired:" + bws.ShotsFired.ToString("F0") + ", Hit:" + bws.ShotsHit.ToString("F0") + ", Acc:" + bws.Accuracy.ToString("F2") + ", HS:" + bws.Headshots.ToString("F0") + ", Time:" + TimeSpan.FromSeconds(bws.TimeEquipped).ToString());
            }
        }
    }

    public class OAuthRequest
    {
        public Uri Url = null;
        public IFlurlRequest FlurlRequest = null;
        InsaneLimits plugin = null;

        HMACSHA1 SHA1 = null;

        public List<KeyValuePair<String, String>> parameters = new List<KeyValuePair<String, String>>();

        public HTTPMethod Method { set; get; }
        public String PostBody { set; get; }

        public OAuthRequest(InsaneLimits plugin, String URL)
        {
            this.plugin = plugin;
            this.Url = new Uri(URL);
            this.FlurlRequest = URL
                .WithHeader("User-Agent", "Mozilla/5.0 (Windows; U; Windows NT 6.1; en-US; rv:1.9.1.3) Gecko/20090824 Firefox/3.5.3 (.NET CLR 4.0.20506)");
        }

        public void Sort()
        {
            // sort the query parameters
            parameters.Sort(delegate (KeyValuePair<String, String> left, KeyValuePair<String, String> right)
            {
                if (left.Key.Equals(right.Key))
                    return left.Value.CompareTo(right.Value);
                else
                    return left.Key.CompareTo(right.Key);
            });
        }

        public String Header()
        {
            String header = "OAuth ";
            List<String> pairs = new List<String>();

            Sort();

            for (Int32 i = 0; i < parameters.Count; i++)
            {

                KeyValuePair<String, String> pair = parameters[i];
                if (pair.Key.Equals("status"))
                    continue;

                pairs.Add(pair.Key + "=\"" + pair.Value + "\"");
            }

            header += String.Join(", ", pairs.ToArray());

            plugin.DebugWrite("OAUTH_HEADER: " + header, 7);

            return header;
        }

        public String Signature(String ConsumerSecret, String AccessTokenSecret)
        {
            String base_url = Url.Scheme + "://" + Url.Host + Url.AbsolutePath;
            String encoded_base_url = UrlEncode(base_url);

            String http_method = Method.ToString();

            Sort();

            List<String> encoded_parameters = new List<String>();
            List<String> raw_parameters = new List<String>();

            // encode and concatenate the query parameters
            for (Int32 i = 0; i < parameters.Count; i++)
            {
                KeyValuePair<String, String> pair = parameters[i];

                // ignore signature if present
                if (pair.Key.Equals("oauth_signature"))
                    continue;

                raw_parameters.Add(pair.Key + "=" + pair.Value);
                encoded_parameters.Add(UrlEncode(pair.Key) + "%3D" + UrlEncode(pair.Value));
            }

            String encoded_query = String.Join("%26", encoded_parameters.ToArray());
            String raw_query = String.Join("&", raw_parameters.ToArray());

            plugin.DebugWrite("HTTP_METHOD: " + http_method, 8);
            plugin.DebugWrite("BASE_URI: " + base_url, 8);
            plugin.DebugWrite("ENCODED_BASE_URI: " + encoded_base_url, 8);
            //plugin.DebugWrite("RAW_QUERY: " + raw_query, 8);
            //plugin.DebugWrite("ENCODED_QUERY: " + encoded_query, 8);

            String base_signature = http_method + "&" + encoded_base_url + "&" + encoded_query;

            plugin.DebugWrite("BASE_SIGNATURE: " + base_signature, 7);

            String HMACSHA1_signature = HMACSHA1_HASH(base_signature, ConsumerSecret, AccessTokenSecret);

            plugin.DebugWrite("HMACSHA1_SIGNATURE: " + HMACSHA1_signature, 7);

            return HMACSHA1_signature;

        }

        public String HMACSHA1_HASH(String text, String ConsumerSecret, String AccessTokenSecret)
        {
            if (SHA1 == null)
            {
                /* Initialize the SHA1 */
                String HMACSHA1_KEY = String.IsNullOrEmpty(ConsumerSecret) ? "" : UrlEncode(ConsumerSecret) + "&" + (String.IsNullOrEmpty(AccessTokenSecret) ? "" : UrlEncode(AccessTokenSecret));
                plugin.DebugWrite("HMACSHA1_KEY: " + HMACSHA1_KEY, 7);
                SHA1 = new HMACSHA1(Encoding.ASCII.GetBytes(HMACSHA1_KEY));
            }

            return Convert.ToBase64String(SHA1.ComputeHash(System.Text.Encoding.ASCII.GetBytes(text)));
        }

        public static String UnreservedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~";

        public static String UrlEncode(String Input)
        {
            StringBuilder Result = new StringBuilder();

            for (Int32 x = 0; x < Input.Length; ++x)
            {
                if (UnreservedChars.IndexOf(Input[x]) != -1)
                    Result.Append(Input[x]);
                else
                    Result.Append("%").Append(String.Format("{0:X2}", (Int32)Input[x]));
            }

            return Result.ToString();
        }

        public static String UrlEncode(Byte[] Input)
        {
            StringBuilder Result = new StringBuilder();

            for (Int32 x = 0; x < Input.Length; ++x)
            {
                if (UnreservedChars.IndexOf((Char)Input[x]) != -1)
                    Result.Append((Char)Input[x]);
                else
                    Result.Append("%").Append(String.Format("{0:X2}", (Int32)Input[x]));
            }

            return Result.ToString();
        }
    }
}
