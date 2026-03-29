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
        public void JoinWith(Thread thread, Int32 secs)
        {
            if (thread == null || !thread.IsAlive)
                return;

            DebugWrite("Waiting for ^b" + thread.Name + "^n to finish", 3);
            thread.Join(secs * 1000);
        }

        public void JoinWith(Thread thread)
        {
            if (thread == null || !thread.IsAlive)
                return;

            JoinWith(thread, 3);
        }




        public void OnPluginDisable()
        {
            if (finalizer != null && finalizer.IsAlive)
                return;

            try
            {

                plugin_enabled = false;
                round_over = false;
                isRoundReset = false;
                level_loaded = false;


                finalizer = new Thread(new ThreadStart(delegate ()
                    {
                        try
                        {
                            DestroyWaitHandles();

                            JoinWith(say_thread);
                            JoinWith(settings_thread);
                            JoinWith(enforcer_thread);
                            JoinWith(moving_thread);
                            JoinWith(fetching_thread, 45);

                            this.blog.CleanUp();
                            this.players.Clear();
                            lock (this.cacheResponseTable)
                            {
                                this.cacheResponseTable.Clear();
                            }

                            CleanupLimits();

                            // unregister the command again to remove availibility-indicator
                            this.UnregisterCommand(match_command_update_plugin_data);

                            ConsoleWrite("^1^bDisabled =(^0");
                        }
                        catch (Exception e)
                        {
                            DumpException(e);
                        }
                    }));

                finalizer.IsBackground = true;
                finalizer.Name = "finalizer";
                finalizer.Start();
                Thread.Sleep(1);
            }
            catch (Exception e)
            {
                DumpException(e);
            }
        }

        public void CleanupLimits()
        {
            List<String> keys = new List<String>(limits.Keys);
            foreach (String key in keys)
            {
                Limit limit = null;
                limits.TryGetValue(key, out limit);
                if (limit == null)
                    continue;

                limit.Reset();
            }

        }


        public List<CPluginVariable> GetDisplayPluginVariables()
        {
            List<CPluginVariable> lstReturn = new List<CPluginVariable>();
            try
            {

                List<String> vars = getPluginVars(true, true, true);

                foreach (String name in vars)
                {
                    String var_name = name;
                    String group_name = getPluginVariableGroup(var_name);
                    String var_type = "multiline";
                    String var_value = getPluginVarValue(var_name);
                    String group_order = getGroupOrder(group_name) + ". ";

                    if (shouldSkipGroup(group_name))
                        continue;

                    if (shouldSkipVariable(var_name, group_name))
                        continue;

                    if (var_name.Contains("password"))
                        var_value = Regex.Replace(var_value, ".", "*");


                    String limit_group_title = String.Empty;
                    Boolean limit_group_visible = true;

                    if (CustomList.isListVar(var_name))
                    {
                        String field = CustomList.extractFieldKey(var_name);
                        String id = CustomList.extractId(var_name);

                        if (!lists.ContainsKey(id))
                            continue;

                        CustomList list = lists[id];

                        if (field.Equals("state"))
                            var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(CustomList.ListState))) + ")";
                        else if (field.Equals("comparison"))
                            var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(CustomList.ListComparison))) + ")";
                        else if (field.Equals("hide"))
                        {
                            var_type = "enum." + var_name + "(...|" + String.Join("|", Enum.GetNames(typeof(ShowHide))) + ")";
                            var_value = "...";
                        }

                        group_order = "";

                    }
                    else if (Limit.isLimitVar(var_name))
                    {
                        String field = Limit.extractFieldKey(var_name);
                        String id = Limit.extractId(var_name);

                        if (!limits.ContainsKey(id))
                            continue;


                        Limit limit = limits[id];

                        if (limit.isGroupFirstField(field))
                            limit_group_title = limit.getGroupFormattedTitleByKey(field);

                        limit_group_visible = limit.getGroupStateByKey(field);


                        if (field.Equals("ea_ban_duration"))
                            var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(EABanDuration))) + ")";
                        else if (field.Equals("pb_ban_duration"))
                            var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(PBBanDuration))) + ")";
                        else if (field.Equals("ea_ban_type"))
                            var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(EABanType))) + ")";
                        else if (field.Equals("pb_ban_type"))
                            var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(PBBanType))) + ")";
                        else if (field.Equals("first_check") || field.Equals("second_check"))
                            var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(Limit.LimitType))) + ")";
                        else if (field.Equals("new_action"))
                        {
                            var_type = "enum." + var_name + "(...|" + String.Join("|", EnumValues(typeof(Limit.LimitAction), true)) + ")";
                            var_value = "...";
                        }
                        else if (field.Equals("evaluation"))
                        {
                            List<String> rawNames = new List<String>(Enum.GetNames(typeof(Limit.EvaluationType)));
                            if (rawNames.Contains("OnIntervalPlayers"))
                            {
                                // move it to the end
                                rawNames.Remove("OnIntervalPlayers");
                                rawNames.Add("OnIntervalPlayers");
                            }
                            if (rawNames.Contains("OnInterval"))
                            {
                                // move it to the end
                                rawNames.Remove("OnInterval");
                                rawNames.Add("OnInterval");
                            }
                            if (rawNames.Contains("OnAnyChat"))
                            {
                                // move to item #3 (will end up being #4)
                                rawNames.Remove("OnAnyChat");
                                rawNames.Insert(2, "OnAnyChat");
                            }
                            if (rawNames.Contains("OnIntervalServer"))
                            {
                                // move to item #3
                                rawNames.Remove("OnIntervalServer");
                                rawNames.Insert(2, "OnIntervalServer");
                            }
                            var_type = "enum." + var_name + "(" + String.Join("|", rawNames.ToArray()) + ")";
                        }
                        else if (field.Equals("say_audience"))
                            var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(MessageAudience))) + ")";
                        else if (field.Equals("say_procon_chat"))
                            var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(TrueFalse))) + ")";
                        else if (field.Equals("state"))
                            var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(Limit.LimitState))) + ")";
                        else if (field.Equals("procon_event_type"))
                            var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(EventType))) + ")";
                        else if (field.Equals("procon_event_name"))
                            var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(CapturableEvent))) + ")";

                        else if (field.Equals("hide"))
                        {
                            var_type = "enum." + var_name + "(...|" + String.Join("|", Enum.GetNames(typeof(ShowHide))) + ")";
                            var_value = "...";
                        }
                        else if (field.Equals("log_destination"))
                            var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(Limit.LimitLogDestination))) + ")";
                        else if (field.Equals("sms_country"))
                            var_type = "enum." + var_name + "(" + String.Join("|", EnumValues(new List<String>(CarriersDict.Keys))) + ")";
                        else if (field.Equals("sms_carrier"))
                        {
                            String country = limit.SMSCountry;

                            if (!CarriersDict.ContainsKey(country))
                                continue;

                            List<String> keys = new List<String>(CarriersDict[country].Keys);

                            // if the carrier does not exist in the country, set the first as default
                            if (!keys.Contains(var_value))
                                var_value = keys[0];

                            limit.SMSCarrier = var_value;

                            var_type = "enum." + var_name + limit.SMSCountry + "(" + String.Join("|", EnumValues(keys)) + ")";
                        }
                        else if (field.Equals("yell_audience"))
                        {
                            var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(MessageAudience))) + ")";
                        }
                        else if (field.Equals("yell_procon_chat"))
                        {
                            var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(TrueFalse))) + ")";
                        }
                        group_order = "";
                    }
                    else if (var_name.Equals("new_limit") || var_name.Equals("new_list"))
                        var_type = "enum." + var_name + "(...|" + String.Join("|", Enum.GetNames(typeof(TrueFalse))) + ")";
                    else if (var_name.Equals("compile_limit"))
                        var_type = "enum." + var_name + "(...|" + String.Join("|", Enum.GetNames(typeof(LimitChoice))) + ")";
                    else if (var_name.Equals("privacy_policy_agreement"))
                    {
                        var_value = "...";
                        var_type = "enum." + var_name + "(...|" + String.Join("|", Enum.GetNames(typeof(AcceptDeny))) + ")";
                    }

                    if (var_name == "rcon_to_battlelog_codes")
                    {
                        if (!getBooleanVarValue("use_slow_weapon_stats")) continue; // hide if use_slow_weapon_stats is False
                        rcon2bw_user_var.Clear();
                        foreach (String k in rcon2bw_user.Keys)
                        {
                            rcon2bw_user_var.Add(k + "=" + rcon2bw_user[k]);
                        }
                        lstReturn.Add(new CPluginVariable(group_order + group_name + "|" + var_name, typeof(String[]), rcon2bw_user_var.ToArray()));

                    }
                    else if (limit_group_title.Length > 0)
                        lstReturn.Add(new CPluginVariable(group_order + group_name + "|" + limit_group_title, "enum.SH(...|" + String.Join("|", Enum.GetNames(typeof(ShowHide))) + ")", "..."));
                    else if (limit_group_visible)
                        lstReturn.Add(new CPluginVariable(group_order + group_name + "|" + var_name, var_type, Uri.EscapeDataString(var_value)));

                }



            }
            catch (Exception e)
            {
                DumpException(e);
            }

            return lstReturn;

        }


        public String[] EnumValues(List<String> names)
        {
            names.Sort(delegate (String left, String right)
            {


                if (left.Equals("None"))
                    return -1;
                else if (right.Equals("None"))
                    return 1;

                return left.CompareTo(right);

            });

            return names.ToArray();
        }

        public String[] EnumValues(Type enum_type, Boolean length_check)
        {
            List<String> names = new List<String>(Enum.GetNames(enum_type));

            names.Sort(delegate (String left, String right)
            {
                if (!length_check)
                    return left.CompareTo(right);

                if (left.Length == right.Length)
                    return left.CompareTo(right);

                else if (left.Equals("None"))
                    return -1;
                else if (right.Equals("None"))
                    return 1;
                else
                    return left.Length.CompareTo(right.Length);
            });

            return names.ToArray();
        }


        public String getPluginVariableGroup(String name)
        {
            foreach (KeyValuePair<String, List<String>> group_pair in settings_group)
                if (group_pair.Value.Contains(name))
                    return group_pair.Key;

            if (CustomList.isListVar(name))
            {
                String listId = CustomList.extractId(name);

                if (!lists.ContainsKey(listId))
                    return "List # {Unknown}";

                CustomList list = lists[listId];


                String max = getMaxListId();
                String format = "List #{0," + max.Length + "} - " + list.Name + " (" + list.State.ToString() + ")";
                return String.Format(format, listId);
            }
            else if (Limit.isLimitVar(name))
            {
                String limitId = Limit.extractId(name);

                if (!limits.ContainsKey(limitId))
                    return "Limit # {Unknown}";

                Limit limit = limits[limitId];

                String cstate = "Compiled";

                if (limit.evaluator == null)
                    cstate = "Not" + cstate;

                String max = getMaxLimitId();
                String format = "Limit #{0," + max.Length + "} - " + limit.Name + " (" + limit.State.ToString() + ", " + cstate + ")";
                return String.Format(format, limitId);
            }
            return SettingsG;
        }

        public Boolean Agreement
        {
            get { return getBooleanVarValue("privacy_policy_agreement"); }
        }


        public const String PrivacyPolicyG = "Custom Privacy Policy";
        public const String WhitelistG = "Whitelist";
        public const String MailG = "Custom SMTP";
        public const String LimitManagerG = "Limit Manager";
        public const String ListManagerG = "Lists Manager";
        public const String StorageG = "Custom Storage";
        public const String TwitterG = "Custom Twitter";
        public const String SettingsG = "Settings";
        public const String ProxyG = "Proxy for HTTP Requests";

        public Boolean shouldSkipGroup(String name)
        {

            if (name.StartsWith(PrivacyPolicyG) && !Agreement)
                return false;

            if (!Agreement)
                return true;

            if (name.StartsWith(WhitelistG) && !getBooleanVarValue("use_white_list"))
                return true;

            if (name.StartsWith(MailG) && !getBooleanVarValue("use_custom_smtp"))
                return true;

            if (name.StartsWith(ListManagerG) && !getBooleanVarValue("use_custom_lists"))
                return true;

            if (name.StartsWith(StorageG) && !getBooleanVarValue("use_custom_storage"))
                return true;

            if (name.StartsWith(TwitterG) && !getBooleanVarValue("use_custom_twitter"))
                return true;

            if (name.StartsWith(ProxyG) && !getBooleanVarValue("use_battlelog_proxy"))
                return true;


            if (name.StartsWith(PrivacyPolicyG) && !getBooleanVarValue("use_custom_privacy_policy"))
                return true;



            return false;
        }

        public Boolean shouldSkipVariable(String name, String group)
        {

            if (name.Equals("privacy_policy_agreement") && Agreement)
                return true;

            if (!Agreement && !group.Equals(PrivacyPolicyG))
                return true;

            if (CustomList.isListVar(name))
            {
                if (!getBooleanVarValue("use_custom_lists"))
                    return true;

                String listId = CustomList.extractId(name);

                if (!lists.ContainsKey(listId))
                    return false;

                return lists[listId].shouldSkipFieldKey(name);
            }
            else if (Limit.isLimitVar(name))
            {
                String limitId = Limit.extractId(name);

                if (!limits.ContainsKey(limitId))
                    return false;

                return limits[limitId].shouldSkipFieldKey(name);
            }


            if (hidden_variables.ContainsKey(name) && hidden_variables[name])
                return hidden_variables[name];

            if (name.Equals("use_slow_weapon_stats") && !getBooleanVarValue("use_direct_fetch")) return true;

            return false;
        }

        public String getGroupOrder(String name)
        {
            Dictionary<Int32, String> reverse = new Dictionary<Int32, String>();
            foreach (KeyValuePair<String, Int32> pair in settings_group_order)
                reverse.Add(pair.Value, pair.Key);

            Int32 offset = 0;
            for (Int32 i = 0; i <= reverse.Count; i++)
                if (!reverse.ContainsKey(i))
                    continue;
                else
                {
                    if (shouldSkipGroup(reverse[i]))
                        continue;
                    offset++;
                    if (name.Equals(reverse[i]))
                        return String.Format("{0,3}", offset.ToString());
                }

            return String.Format("{0,3}", offset.ToString());
        }

        public List<CPluginVariable> GetPluginVariables()
        {
            List<CPluginVariable> lstReturn = new List<CPluginVariable>();

            List<String> vars = getPluginVars(false, false, false, false);
            foreach (String var in vars)
            {
                if (var == "rcon_to_battlelog_codes")
                {
                    rcon2bw_user_var.Clear();
                    foreach (String k in rcon2bw_user.Keys)
                    {
                        rcon2bw_user_var.Add(k + "=" + rcon2bw_user[k]);
                    }
                    lstReturn.Add(new CPluginVariable(var, typeof(String[]), this.rcon2bw_user_var.ToArray()));
                }
                else
                {
                    lstReturn.Add(new CPluginVariable(var, typeof(String), "BASE64:" + Encode(getPluginVarValue(var))));
                }
            }

            return lstReturn;
        }

        public void SetPluginVariable(String var, String val)
        {
            try
            {
                if (var == "rcon_to_battlelog_codes")
                {
                    rcon2bw_user_var = new List<String>(CPluginVariable.DecodeStringArray(val));
                    rcon2bw_user.Clear();
                    foreach (String item in rcon2bw_user_var)
                    {
                        if (String.IsNullOrEmpty(item)) continue;
                        String[] entry = item.Split(new Char[] { '=' });
                        if (entry.Length != 2)
                        {
                            DebugWrite("^1^bWARNING:^n^0 rcon_to_battlelog_codes item '" + item + "' is malformed, ignoring", 3);
                            continue;
                        }
                        String key = entry[0].Trim();
                        String code = entry[1].Trim();
                        DebugWrite("Added weapon code[^b" + key + "^n] = ^b" + code + "^n", 3);
                        rcon2bw_user[key] = code;
                    }
                    return;
                }
                String decoded = val;
                Boolean ui = true;
                if (decoded.StartsWith("BASE64:"))
                {
                    decoded = decoded.Replace("BASE64:", "");
                    decoded = Decode(decoded);
                    ui = false;
                }

                setPluginVarValue(var, decoded, ui);
            }
            catch (Exception e)
            {
                DumpException(e);
            }
        }

    }
}
