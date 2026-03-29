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
        public void InitWaitHandles()
        {
            DebugWrite("Initializing wait handles", 6);
            fetch_handle = new EventWaitHandle(false, EventResetMode.ManualReset);
            enforcer_handle = new EventWaitHandle(false, EventResetMode.ManualReset);
            settings_handle = new EventWaitHandle(false, EventResetMode.ManualReset);
            say_handle = new EventWaitHandle(false, EventResetMode.ManualReset);
            info_handle = new EventWaitHandle(false, EventResetMode.ManualReset);
            scratch_handle = new EventWaitHandle(false, EventResetMode.ManualReset);
            list_handle = new EventWaitHandle(false, EventResetMode.ManualReset);
            indices_handle = new EventWaitHandle(false, EventResetMode.ManualReset);
            server_name_handle = new EventWaitHandle(false, EventResetMode.ManualReset);
            server_desc_handle = new EventWaitHandle(false, EventResetMode.ManualReset);
            plist_handle = new EventWaitHandle(false, EventResetMode.ManualReset);
            move_handle = new EventWaitHandle(false, EventResetMode.ManualReset);
            pending_handle = new EventWaitHandle(false, EventResetMode.ManualReset);
            reply_handle = new EventWaitHandle(false, EventResetMode.ManualReset);
        }

        public void DestroyWaitHandles()
        {

            if (fetch_handle != null)
                fetch_handle.Set();
            if (enforcer_handle != null)
                enforcer_handle.Set();
            if (settings_handle != null)
                settings_handle.Set();
            if (say_handle != null)
                say_handle.Set();
            if (info_handle != null)
                info_handle.Set();
            if (scratch_handle != null)
                scratch_handle.Set();

            if (list_handle != null)
                list_handle.Set();
            if (indices_handle != null)
                indices_handle.Set();
            if (server_name_handle != null)
                server_name_handle.Set();
            if (server_desc_handle != null)
                server_desc_handle.Set();
            if (activate_handle != null)
                activate_handle.Set();

            if (plist_handle != null)
                plist_handle.Set();
            if (move_handle != null)
                move_handle.Set();
            if (pending_handle != null)
                pending_handle.Set();
            if (reply_handle != null)
                reply_handle.Set();

            plugin_activated = false;

        }

        public void InitThreads()
        {
            DebugWrite("Initializing threads", 6);
            this.fetching_thread = new Thread(new ThreadStart(fetch_thread_loop));
            this.enforcer_thread = new Thread(new ThreadStart(enforcer_thread_loop));
            this.say_thread = new Thread(new ThreadStart(say_thread_loop));
            this.settings_thread = new Thread(new ThreadStart(settings_thread_loop));
            this.moving_thread = new Thread(new ThreadStart(move_thread_loop));

            this.fetching_thread.IsBackground = true;
            this.enforcer_thread.IsBackground = true;
            this.say_thread.IsBackground = true;
            this.settings_thread.IsBackground = true;
            this.moving_thread.IsBackground = true;
        }

        public void StartThreads()
        {
            DebugWrite("Starting threads", 6);
            settings_thread.Start();
            say_thread.Start();
            enforcer_thread.Start();
            fetching_thread.Start();
            moving_thread.Start();
        }

        public void ActivatePlugin()
        {

            plugin_activated = true;
            activate_handle.Set();

            InitWeapons();
            InitReplacements();

            InitWaitHandles();
            InitThreads();

            ClearData();

            // Initial commands
            getMapInfoSync();
            getServerNameSync();
            getServerDescriptionSync();

            StartThreads();

            getPlayersList();
            getPBPlayersList();
            getReservedSlotsList();

            DelayedCompile(30);

            Int32 lc = limits.Count;
            String lc_msg = "limit" + ((lc > 1 || lc == 0) ? "s" : "");

            if (getBooleanVarValue("tweet_my_plugin_state"))
                DefaultTweet("#InsaneLimits #plugin enabled  @\"" + server_name + "\", using " + lc + " " + lc_msg);
        }

        public void DelayedCompile(Int32 sleep_time)
        {
            //delayed limit compilation
            Thread delayed_compilation = new Thread(new ThreadStart(delegate ()
            {
                DebugWrite("sleeping for " + sleep_time + " seconds, before compiling limits", 4);
                Thread.Sleep(sleep_time * 1000);
                CompileAll();

            }));

            delayed_compilation.IsBackground = true;
            delayed_compilation.Name = "delayed_comp";
            delayed_compilation.Start();
        }

        public void ClearData()
        {
            if (serverInfo != null)
                serverInfo.Data.Clear();

            List<String> keys = new List<String>();
            foreach (String key in keys)
                if (limits.ContainsKey(key))
                    limits[key].Data.Clear();

            Data.Clear();
        }


        Dictionary<String, PlayerInfo> new_players_batch = new Dictionary<String, PlayerInfo>();

        private Int32 GetQCount()
        {
            Int32 npqc = 0;
            lock (players_mutex)
            {
                npqc = new_player_queue.Count;
            }
            return npqc;
        }

        private Int32 GetBCount()
        {
            Int32 npbc = 0;
            lock (players_mutex)
            {
                npbc = new_players_batch.Count;
            }
            return npbc;
        }

        public void fetch_thread_loop()
        {

            try
            {

                Thread.CurrentThread.Name = "fetch";
                DebugWrite(" starting", 4);

                InsaneLimits plugin = this;

                Dictionary<String, Int32> retryCount = new Dictionary<String, Int32>();
                Dictionary<String, CPunkbusterInfo> retryInfo = new Dictionary<String, CPunkbusterInfo>();
                Boolean gaveEnforcerTime = false;

                /*
                In order to reduce the rate of fetches to avoid "Too Many Requests"
                errors, apply a lower bound on the amount of time used to do one
                fetch and one insert. Any remaining time is spent sleeping.
                The value for minSecs is adaptive. The more errors there are,
                the longer it gets. Each success reduces it back.
                
                According to
                
                http://www.phogue.net/forumvb/showthread.php?5313-Battlelog-stats-make-plugins-amp-procon-lag-Solution-Global-stats-fetching&p=62259&viewfull=1#post62259
                
                the upper bound for request rate is 15 requests every 20 seconds.
                To allow head-room for other plugins running simultaneously,
                we cap our rate at 5 requests every 20 seconds. For a full
                server of 64 players, that means a minimum time to empty the
                initial fetch queue is about 4.5 minutes.
                */
                DateTime since = DateTime.Now; // lower bound
                Double minSecs = 4.0; // min between fetches
                Double maxSecs = 10.0; // max between fetches
                Double lowerBound = minSecs;

                while (true)
                {
                    gaveEnforcerTime = false;

                    while (GetQCount() == 0)
                    {
                        if (retryCount.Count > 0)
                        {
                            foreach (String k in retryCount.Keys)
                            {
                                lock (players_mutex)
                                {
                                    if (!new_player_queue.ContainsKey(k))
                                    {
                                        new_player_queue.Add(k, retryInfo[k]);
                                    }
                                }
                            }
                            DebugWrite("Retrying fetch for ^b" + retryCount.Count + "^n players in the retry queue", 4);
                            continue;
                        }
                        // if there are no more players, put yourself to sleep
                        DebugWrite("no new players, will wait, signalling ^benforcer^n thread", 7);
                        fetch_handle.Reset();
                        gaveEnforcerTime = true;
                        enforcer_handle.Set();
                        WaitOn("fetch_handle", fetch_handle);
                        fetch_handle.Reset();
                        if (!plugin_enabled) break;
                        DebugWrite("awake! checking queue ...", 7);
                        if (GetQCount() == 0) DebugWrite("Nothing to do, ^bfetch^n going back to sleep...", 7);
                    }

                    DateTime fetchSince = DateTime.Now;

                    if (!gaveEnforcerTime && plugin_enabled)
                    {
                        // Give some time to enforcer thread
                        DebugWrite("players in fetch queue, giving time to ^benforcer^n thread", 7);
                        fetch_handle.Reset();
                        gaveEnforcerTime = true;
                        enforcer_handle.Set();
                        WaitOn("fetch_handle", fetch_handle);
                        fetch_handle.Reset();
                        DebugWrite("awake!, block ^benforcer^n thread", 7);
                    }

                    while (GetQCount() > 0)
                    {
                        if (!plugin_enabled)
                            break;

                        List<String> keys = null;

                        lock (players_mutex)
                        {
                            keys = new List<String>(new_player_queue.Keys);
                        }

                        String name = keys[0];

                        CPunkbusterInfo info = null;

                        lock (players_mutex)
                        {
                            new_player_queue.TryGetValue(name, out info);
                        }

                        if (info == null)
                        {
                            lock (players_mutex)
                            {
                                if (new_player_queue.ContainsKey(name)) new_player_queue.Remove(name);
                            }
                            continue;
                        }

                        // make sure I am the only one modifying these dictionaries at this time
                        lock (players_mutex)
                        {
                            if (new_player_queue.ContainsKey(name))
                                new_player_queue.Remove(name);

                            if (!new_players_batch.ContainsKey(name))
                                new_players_batch.Add(name, null);
                        }

                        Int32 nq = GetQCount();
                        String msg = nq + " more player" + ((nq > 1) ? "s" : "") + " in queue";
                        if (nq == 0) msg = "no more players in queue";

                        Boolean ck = false;
                        lock (players_mutex)
                        {
                            ck = new_players_batch.ContainsKey(info.SoldierName);
                        }
                        if (ck)
                        {

                            if (lowerBound > minSecs && DateTime.Now.Subtract(since).TotalSeconds < lowerBound)
                            {
                                // Add some delay between consecutive fetches
                                DebugWrite("adding delay before next fetch, lower bound is " + lowerBound + " secs", 5);
                                Double upperBound = maxSecs * 2;
                                while (DateTime.Now.Subtract(since).TotalSeconds < lowerBound && upperBound > 0.0)
                                {
                                    if (!plugin_enabled) break;
                                    // Give some time to enforcer thread
                                    fetch_handle.Reset();
                                    DebugWrite("throttling fetch, giving time to ^benforcer^n thread", 7);
                                    gaveEnforcerTime = true;
                                    enforcer_handle.Set();
                                    WaitOn("fetch_handle", fetch_handle);
                                    fetch_handle.Reset();
                                    DebugWrite("awake, check throttling delay", 7);
                                    upperBound = upperBound - 1.0;
                                }
                                DebugWrite("awake, proceeding with next fetch", 5);
                            }

                            DebugWrite("^4getting battlelog stats for ^b" + name + "^n, " + msg + "^0", 5);
                            since = DateTime.Now; // reset timer
                            PlayerInfo ptmp = plugin.blog.fetchStats(new PlayerInfo(plugin, info));

                            /* If there was a fetch error, remember for retry */
                            if (ptmp._web_exception != null)
                            {
                                // Adaptively increment
                                lowerBound = Math.Min(lowerBound + 1.0, maxSecs);
                                if (lowerBound != maxSecs) DebugWrite("increase lower bound to " + lowerBound.ToString("F0") + " secs", 6);

                                lock (players_mutex)
                                {
                                    if (new_players_batch.ContainsKey(name)) new_players_batch.Remove(name);
                                }

                                // Check if player still present
                                Boolean sheLeft = false;
                                lock (players_mutex)
                                {
                                    sheLeft = (!scratch_list.Contains(name));
                                }
                                if (sheLeft)
                                {
                                    DebugWrite("aborting fetch, looks like player " + name + " left the game!", 6);
                                    continue;
                                }

                                if (!retryCount.ContainsKey(name))
                                {
                                    retryCount[name] = 0;
                                    retryInfo[name] = info;
                                    ptmp = null; // release failed fetch info
                                    DebugWrite("^b" + name + "^n is one of ^b" + retryCount.Count + "^n players in the retry queue", 5);
                                    continue;
                                }
                                retryCount[name] = retryCount[name] + 1;
                                DebugWrite("Retry " + retryCount[name] + " for " + name, 4);
                                if (retryCount[name] >= 3)
                                {
                                    // give up
                                    retryCount.Remove(name);
                                    retryInfo.Remove(name);
                                    if (ptmp._web_exception == null) ptmp._web_exception = new System.Net.WebException("fetch retry failed");
                                    DebugWrite("Fetching stats for ^b" + name + "^n: " + ptmp._web_exception.Message, 4);
                                }
                                else
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                // Adaptively decrement
                                lowerBound = Math.Max(lowerBound - 1.0, minSecs);
                                if (lowerBound != minSecs) DebugWrite("decrease lower bound to " + lowerBound.ToString("F0") + " secs", 5);

                                if (retryCount.ContainsKey(name))
                                {
                                    retryCount.Remove(name);
                                    retryInfo.Remove(name);
                                }
                            }

                            lock (players_mutex)
                            {
                                new_players_batch[name] = ptmp;
                            }

                            if (ptmp.StatsError)
                            {
                                DebugWrite("Unable to fetch stats for ^b" + name + "^n", 4);
                            }
                        }

                        if (!plugin_enabled) break;

                        if (GetBCount() > 0)
                        {
                            break;
                        }
                    }

                    DebugWrite("^4^bTIME^n took " + DateTime.Now.Subtract(fetchSince).TotalSeconds.ToString("F2") + " secs, fetching players from queue", 5);

                    // abort the thread if the plugin was disabled
                    if (!plugin_enabled)
                    {
                        DebugWrite("detected that plugin was disabled, aborting", 4);
                        lock (players_mutex)
                        {
                            new_player_queue.Clear();
                            new_players_batch.Clear();
                        }
                        return;
                    }

                    Int32 bb = GetBCount();

                    DateTime batchSince = DateTime.Now;

                    if (bb > 0)
                    {
                        DebugWrite("done fetching stats, " + bb + " player" + ((bb > 1) ? "s" : "") + " in new batch, updating player's list", 5);

                        // Async request for updates
                        scratch_handle.Reset();
                        plist_handle.Reset();
                        DebugWrite("waiting for player list updates", 5);
                        getPlayersList();
                        WaitOn("scratch_handle", scratch_handle);
                        scratch_handle.Reset();
                        WaitOn("plist_handle", plist_handle);
                        plist_handle.Reset();
                        DebugWrite("awake! got player list updates", 5);
                    }

                    List<PlayerInfo> inserted = new List<PlayerInfo>();
                    // first insert the entire player's batch

                    lock (players_mutex)
                    {
                        // remove the nulls, and the ones that left
                        List<String> players_to_remove = new List<String>();
                        foreach (KeyValuePair<String, PlayerInfo> pair in new_players_batch)
                            if (pair.Value == null || !scratch_list.Contains(pair.Key))
                                if (!players_to_remove.Contains(pair.Key))
                                {
                                    plugin.DebugWrite("looks like ^b" + pair.Key + "^n left, removing him from new batch", 5);
                                    players_to_remove.Add(pair.Key);
                                }


                        // now remove them
                        foreach (String pname in players_to_remove)
                            if (new_players_batch.ContainsKey(pname))
                                new_players_batch.Remove(pname);

                        if (new_players_batch.Count > 0)
                        {
                            bb = new_players_batch.Count;
                            DebugWrite("Will insert a batch of " + bb + " player" + ((bb > 1) ? "s" : ""), 5);
                        }
                        else bb = 0;


                        foreach (KeyValuePair<String, PlayerInfo> pair in new_players_batch)
                            if (pair.Value != null && scratch_list.Contains(pair.Key))
                            {
                                //players.Add(pair.Key, pair.Value);
                                if (players.ContainsKey(pair.Key))
                                {
                                    DebugWrite("--------->>> Why does players dict already have " + pair.Key + " in it????", 5);
                                }
                                players[pair.Key] = pair.Value;
                                inserted.Add(pair.Value);
                            }

                        new_players_batch.Clear();
                    }

                    // abort the thread if the plugin was disabled
                    if (!plugin_enabled)
                    {
                        DebugWrite("detected that plugin was disabled, aborting", 4);
                        lock (players_mutex)
                        {
                            new_player_queue.Clear();
                            new_players_batch.Clear();
                        }
                        return;
                    }

                    // then for each of the players just inserted, evaluate OnJoin
                    if (bb > 0)
                    {
                        DebugWrite("For " + bb + " new players, evaluate OnJoin limits", 5);
                        foreach (PlayerInfo pp in inserted)
                        {
                            OnPlayerJoin(pp); // each call syncs map info
                            // quit early if plugin was disabled
                            if (!plugin_enabled)
                                break;
                        }
                    }
                    else
                    {
                        DebugWrite("No players left in batch, skipping OnJoin limits, synching map info", 5);
                        getMapInfoSync();
                    }

                    DebugWrite("^4^bTIME^n took " + DateTime.Now.Subtract(batchSince).TotalSeconds.ToString("F2") + " secs, process player batch", 5);


                    // abort the thread if the plugin was disabled
                    if (!plugin_enabled)
                    {
                        DebugWrite("detected that plugin was disabled, aborting", 4);
                        lock (players_mutex)
                        {
                            new_player_queue.Clear();
                            new_players_batch.Clear();
                        }
                        return;
                    }

                    DebugWrite("Request PB player's list for new players to queue ...", 8);
                    getPBPlayersList();

                    DebugWrite("^4^bDONE^n inserting " + bb + " new players, " + GetQCount() + " still in queue, took a total of " + DateTime.Now.Subtract(since).TotalSeconds.ToString("F0") + " secs^0", 3);
                }
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
            finally
            {
                enforcer_handle.Set();
            }

        }
        public Int32 getLineOffset(String haystack, String needle)
        {
            Int32 start_line = 0;
            String[] lines = haystack.Split(new String[] { Environment.NewLine }, StringSplitOptions.None);
            foreach (String line in lines)
                if (++start_line > 0 && Regex.Match(line, needle).Success)
                    break;

            return start_line;
        }



        public class SayMessage
        {
            public MessageAudience audience = MessageAudience.All;
            public String text = String.Empty;
            public Int32 TeamId = 0;
            public Int32 SquadId = 0;
            public String player = String.Empty;

            public SayMessage(Int32 TeamId, Int32 SquadId, String player, MessageAudience audience, String text)
            {
                this.audience = audience;
                this.text = text;
                this.SquadId = SquadId;
                this.TeamId = TeamId;
                this.player = player;
            }
        }

        public Queue<SayMessage> messageQueue = new Queue<SayMessage>();


        public void QueueSayMessage(SayMessage message)
        {
            lock (message_mutex)
            {
                messageQueue.Enqueue(message);
            }
            say_handle.Set();
        }

        public void SendQueuedMessages(Int32 sleep_time)
        {

            DebugWrite("sending " + messageQueue.Count + " queued message" + ((messageQueue.Count > 1) ? "s" : "") + " ...", 7);

            while (messageQueue.Count > 0)
            {
                Thread.Sleep(sleep_time);
                if (!plugin_enabled)
                    return;

                SayMessage message = messageQueue.Dequeue();
                switch (message.audience)
                {
                    case MessageAudience.All:
                        SendGlobalMessageV(message.text);
                        break;
                    case MessageAudience.Team:
                        SendTeamMessageV(message.TeamId, message.text);
                        break;
                    case MessageAudience.Squad:
                        SendSquadMessageV(message.TeamId, message.SquadId, message.text);
                        break;
                    case MessageAudience.Player:
                        SendPlayerMessageV(message.player, message.text);
                        break;
                    default:
                        ConsoleError(message.audience.ToString() + " is not known for " + message.audience.GetType().Name);
                        break;
                }
            }
        }


        public void say_thread_loop()
        {

            try
            {
                InsaneLimits plugin = this;

                Thread.CurrentThread.Name = "say";

                plugin.DebugWrite("starting", 4);
                while (true)
                {

                    Int32 sleep_time = (Int32)(getFloatVarValue("say_interval") * 1000f);

                    if (messageQueue.Count == 0)
                    {
                        DebugWrite("waiting for say message ...", 7);
                        say_handle.WaitOne();
                        say_handle.Reset();
                        DebugWrite("awake!", 7);
                    }

                    SendQueuedMessages(sleep_time);

                    if (!plugin_enabled)
                        break;
                }


                // abort the thread if the plugin was disabled
                if (!plugin_enabled)
                {
                    plugin.DebugWrite("detected that plugin was disabled, aborting", 4);
                    return;
                }

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

        public void settings_thread_loop()
        {
            try
            {
                InsaneLimits plugin = this;

                Thread.CurrentThread.Name = "settings";

                plugin.DebugWrite("starting", 4);
                ConsoleWrite(" Version = " + GetPluginVersion());

                while (true)
                {
                    try
                    {
                        Int32 sleep_t = getIntegerVarValue("auto_load_interval");
                        plugin.DebugWrite("sleeping for ^b" + sleep_t + "^n second" + ((sleep_t > 1) ? "s" : "") + ", before next iteration", 7);

                        settings_handle.Reset();
                        settings_handle.WaitOne(sleep_t * 1000);
                        plugin.DebugWrite("awake! loading settings", 7);


                        if (!plugin_enabled)
                            break;

                        LoadSettings(false, true);
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

                    if (!plugin_enabled)
                        break;
                }

                // abort the thread if the plugin was disabled
                if (!plugin_enabled)
                {
                    plugin.DebugWrite("detected that plugin was disabled, aborting", 4);
                    return;
                }
            }
            catch (Exception e)
            {
                DumpException(e);
            }
        }

        enum WhichTimer { Squad, Vars };

        private void resetUpdateTimer(WhichTimer t)
        {
            switch (t)
            {
                case WhichTimer.Squad:
                    lock (updates_mutex)
                    {
                        timerSquad = DateTime.Now;
                    }
                    break;
                case WhichTimer.Vars:
                    lock (updates_mutex)
                    {
                        timerVars = DateTime.Now;
                    }
                    break;
            }
        }

        public void enforcer_thread_loop()
        {

            try
            {
                InsaneLimits plugin = this;
                enforcer_handle.Reset();

                Thread.CurrentThread.Name = "enforcer";

                plugin.DebugWrite("starting", 4);

                resetUpdateTimer(WhichTimer.Vars);

                while (true)
                {

                    DebugWrite("waiting for signal from ^bfetch^n thread", 7);
                    Thread.Sleep(1000);

                    // Wait for fetch thread to let us go through
                    enforcer_handle.WaitOne();
                    enforcer_handle.Reset();
                    DateTime now = DateTime.Now;

                    DebugWrite("awake! Checking update timers  ...", 7);

                    DateTime tv = DateTime.Now;

                    lock (plugin.updates_mutex)
                    {
                        tv = plugin.timerVars;
                    }

                    if (DateTime.Now.Subtract(tv).TotalSeconds > getFloatVarValue("update_interval"))
                    {
                        plugin.updateVars();
                    }

                    DebugWrite("Processing interval limits ...", 7);

                    if (!plugin_enabled)
                        break;

                    try
                    {

                        List<Limit> sorted_limits = getLimitsForEvaluation(Limit.EvaluationType.OnInterval | Limit.EvaluationType.OnIntervalPlayers | Limit.EvaluationType.OnIntervalServer);

                        if (sorted_limits.Count == 0)
                        {
                            plugin.DebugWrite("No valid ^bOnIntervalPlayers^n or ^bOnIntervalServer^n  limits founds, skipping this iteration", 8);
                            continue;
                        }

                        //Remove all limit for which there is still remaining time
                        sorted_limits.RemoveAll(delegate (Limit limit) { return limit.RemainingSeconds(now) > 0; });

                        /*
                        After OnLevelLoaded, if there is an interval limit to
                        evaluate and we haven't seen the first spawn yet
                        and the round was reset;
                        the first limit to evaluate "dirties" the round,
                        and therefore it is no longer reset.
                        */
                        if (isRoundReset && sorted_limits.Count > 0)
                        {
                            DebugWrite("Round NEEDS resetting!", 8);
                            isRoundReset = false;
                        }


                        if (sorted_limits.Count == 0)
                        {
                            continue;
                        }
                        else
                        {
                            DebugWrite("signal from ^bfetch^n received and " + sorted_limits.Count + " interval limits ready to fire ...", 7);
                        }


                        // make sure we are the only ones scanning the player's list
                        List<String> sorted_players = null;
                        lock (players_mutex)
                        {
                            sorted_players = new List<String>(players.Keys);
                        }

                        // sort the players in by join time in descending order
                        sorted_players.Sort(sort_players_t_desc_cmp);
                        DumpPlayersList(sorted_players, 10);


                        for (Int32 i = 0; i < sorted_limits.Count; i++)
                        {
                            if (!plugin_enabled)
                                break;

                            Limit limit = sorted_limits[i];

                            if (limit == null)
                                continue;

                            Limit.EvaluationType type = limit.Evaluation;

                            // skip limit if there are no players in the server
                            if (type.Equals(Limit.EvaluationType.OnIntervalPlayers) && sorted_players.Count == 0)
                                continue;

                            // refresh the map information before each limit evaluation
                            getMapInfoSync();

                            if (type.Equals(Limit.EvaluationType.OnIntervalPlayers) && sorted_players.Count > 0)
                            {
                                DebugWrite("Evaluating " + limit.ShortDisplayName + " for " + sorted_players.Count + " player" + ((sorted_players.Count > 1) ? "s" : ""), 6);

                                for (Int32 j = 0; j < sorted_players.Count; j++)
                                {
                                    if (!plugin_enabled)
                                        break;

                                    String name = sorted_players[j];
                                    PlayerInfo pinfo = null;
                                    if (players.ContainsKey(name))
                                        pinfo = players[name];

                                    // if there are no stats, skip this player
                                    if (pinfo == null)
                                        continue;


                                    plugin.DebugWrite("Evaluating " + limit.ShortDisplayName + " for ^b" + name + "^n", 6);

                                    if (evaluateLimit(limit, pinfo))
                                    {
                                        // refresh server information if evaluation was successful
                                        plugin.DebugWrite("Waiting for server information before proceeding", 7);
                                        getServerInfoSync();
                                    }
                                }

                            }
                            else if (type.Equals(Limit.EvaluationType.OnIntervalServer))
                            {
                                DebugWrite("Evaluating " + limit.ShortDisplayName + " - " + limit.Evaluation.ToString(), 5);
                                evaluateLimit(limit);
                            }
                        }
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
                    finally
                    {
                        // Notify the fetch thread
                        DebugWrite("done, will signal ^bfetch^n thread", 7);
                        fetch_handle.Set();
                    }

                    if (!plugin_enabled)
                        break;

                }


                // abort the thread if the plugin was disabled
                if (!plugin_enabled)
                {
                    plugin.DebugWrite("detected that plugin was disabled, aborting", 4);
                    return;
                }

            }
            catch (Exception e)
            {
                DumpException(e);
            }
            finally
            {
                fetch_handle.Set(); // let fetch handle abort
            }
        }

        public void getServerInfo()
        {
            ServerCommand("serverInfo");
        }

        public ServerInfo getServerInfoSync()
        {
            info_handle.Reset();
            getServerInfo();
            Thread.Sleep(500);
            WaitOn("info_handle", info_handle);
            info_handle.Reset();

            return this.serverInfo;
        }



        public void getPlayersList()
        {
            ServerCommand("admin.listPlayers", "all");
            getServerInfo();

        }

        public void getPlayersListSync()
        {
            ServerCommand("admin.listPlayers", "all");


        }

        public void getPBPlayersList()
        {
            if (this.players.Keys.Count > 0)
            {
                expectedPBCount = this.players.Keys.Count + 1; // FIXME, not locked on purpose
            }
            else
            {
                expectedPBCount = 64;
            }
            ServerCommand("punkBuster.pb_sv_command", "pb_sv_plist");
            getServerInfo();
        }

        public void getReservedSlotsList()
        {
            ServerCommand("reservedSlotsList.list");
        }

        public static List<String> getExtraFields()
        {
            List<String> fields = new List<String>();
            fields.Add("recon_t");
            fields.Add("engineer_t");
            fields.Add("assault_t");
            fields.Add("support_t");
            fields.Add("vehicle_t");
            fields.Add("recon_p");
            fields.Add("engineer_p");
            fields.Add("assault_p");
            fields.Add("support_p");
            fields.Add("vehicle_p");

            return fields;

        }

        // Battlelog JSON key name, to Plugin field name
        public static String JSON2Key(String key, String game_version)
        {
            Dictionary<String, String> j2k = null;
            switch (game_version)
            {
                case "BF3":
                    j2k = json2key;
                    break;
                case "BF4":
                    j2k = json2keyBF4;
                    break;
                case "BFHL":
                default:
                    j2k = json2keyBF4; // TBH BFH
                    break;
            }

            if (!j2k.ContainsKey(key))
                throw new StatsException("unknown JSON field ^b" + key + "^b");
            return j2k[key];
        }

        public static String WJSON2Prop(String key)
        {
            if (!wjson2prop.ContainsKey(key))
                throw new StatsException("unknown Weapon JSON field ^b" + key + "^b");
            return wjson2prop[key];
        }

        public static List<String> getBasicJSONFieldKeys(String game_version)
        {
            if (game_version == "BF3")
                return new List<String>(json2key.Keys);
            else
                return new List<String>(json2keyBF4.Keys); // TBD BFH
        }

        public static List<String> getBasicWJSONFieldKeys()
        {
            return new List<String>(wjson2prop.Keys);
        }

        public static List<String> getBasicFieldKeys(String game_version)
        {
            if (game_version == "BF3")
                return new List<String>(json2key.Values);
            else
                return new List<String>(json2keyBF4.Values); // TBD BFH
        }

        public static List<String> getBasicWeaponFieldProps()
        {
            return new List<String>(wjson2prop.Values);
        }

        public static List<String> getGameFieldKeys()
        {
            return new List<String>(gamekeys.Keys);
        }


        public Boolean setPluginVarValue(String var, String val)
        {
            return setPluginVarValue(null, var, val, false);
        }

        public Boolean setPluginVarValue(String var, String val, Boolean ui)
        {
            return setPluginVarValue(null, var, val, ui);
        }

        public Boolean setPluginVarValue(String sender, String var, String val)
        {
            return setPluginVarValue(sender, var, val, false);
        }

        public Boolean setPluginVarValue(String sender, String var, String val, Boolean ui)
        {
            try
            {
                if (var == null || val == null || var == "rcon_to_battlelog_codes")
                    return false;

                if (!isPluginVar(var))
                {
                    SendConsoleError(sender, "unknown variable \"" + var + "\"");
                    return false;
                }



                /* Parse Integer Values */
                Int32 integerValue = 0;
                Boolean isIntegerValue = Int32.TryParse(val, out integerValue);


                /* Parse Boolean Values */
                Boolean booleanValue = false;
                Boolean isBooleanValue = true;

                if (Regex.Match(val, @"^\s*(1|true|yes|accept)\s*$", RegexOptions.IgnoreCase).Success)
                    booleanValue = true;
                else if (Regex.Match(val, @"^\s*(0|false|no|deny|\.\.\.)\s*$", RegexOptions.IgnoreCase).Success)
                    booleanValue = false;
                else
                    isBooleanValue = false;


                /* Parse Float Values */
                Single floatValue = 0F;
                Boolean isFloatValue = Single.TryParse(val, out floatValue);


                /* Parse String List */
                List<String> stringListValue = new List<String>(Regex.Split(val.Replace(";", ",").Replace("|", ","), @"\s*,\s*"));
                Boolean isStringList = true;


                /* Parse String var */
                String stringValue = val;
                Boolean isStringValue = (val != null);

                if (Limit.isLimitVar(var))
                    return setLimitVarValue(var, val, ui);
                else if (CustomList.isListVar(var))
                    return setListVarValue(var, val, ui);

                else if (isBooleanVar(var))
                {
                    if (!isBooleanValue)
                    {
                        SendConsoleError(sender, "\"" + val + "\" is invalid for ^b" + var);
                        return false;
                    }

                    if (val.Equals("..."))
                        return false;

                    return setBooleanVarValue(var, booleanValue);
                }
                else if (isIntegerVar(var))
                {
                    if (!isIntegerValue)
                    {
                        SendConsoleError(sender, "\"" + val + "\" is invalid for " + var);
                        return false;
                    }

                    setIntegerVarValue(var, integerValue);
                    return true;
                }
                else if (isFloatVar(var))
                {
                    if (!isFloatValue)
                    {
                        SendConsoleError(sender, "\"" + val + "\" is invalid for ^b" + var);
                        return false;
                    }

                    return setFloatVarValue(var, floatValue);
                }
                else if (isStringListVar(var))
                {
                    if (!isStringList)
                    {
                        SendConsoleError(sender, "\"" + val + "\"  is invalid for " + var);
                        return false;
                    }

                    setStringListVarValue(var, stringListValue);
                    return true;
                }
                else if (isStringVar(var))
                {
                    if (!isStringValue)
                    {
                        SendConsoleError(sender, "invalid value for " + var);
                        return false;
                    }

                    setStringVarValue(var, stringValue);
                    return true;
                }
                else
                {
                    SendConsoleError(sender, "unknown variable ^b" + var);
                    return false;
                }
            }
            catch (Exception e)
            {
                DumpException(e);
            }
            finally
            {
                if (ui)
                    SaveSettings(true);
            }

            return false;
        }



        public String getLimitVarId(String var)
        {
            if (!Limit.isLimitVar(var))
            {
                ConsoleError("^b" + var + "^n is not a limit variable");
                return "";
            }
            return Limit.extractId(var);
        }

        public String getListVarId(String var)
        {
            if (!CustomList.isListVar(var))
            {
                ConsoleError("^b" + var + "^n is not a list variable");
                return "";
            }
            return CustomList.extractId(var);
        }

        public String getListVarValue(String var)
        {
            String listId = getListVarId(var);
            if (listId.Length == 0)
                return "";

            if (!lists.ContainsKey(listId))
            {
                ConsoleError("there are no lists with ^bid^n(" + listId + ")");
                return "";
            }

            CustomList list = lists[listId];

            return list.getField(var).ToString();
        }


        public String getLimitVarValue(String var)
        {
            String limitId = getLimitVarId(var);
            if (limitId.Length == 0)
                return "";

            if (!limits.ContainsKey(limitId))
            {
                ConsoleError("there are no limits with ^bid^n(" + limitId + ")");
                return "";
            }

            Limit limit = limits[limitId];

            return limit.getField(var).ToString();
        }

        public Boolean setListVarValue(String var, String val, Boolean ui)
        {
            try
            {

                String listId = getListVarId(var);
                if (listId.Length == 0)
                    return false;

                CustomList list = null;
                if (lists.ContainsKey(listId))
                    list = lists[listId];
                else
                {
                    list = new CustomList(this, listId);
                    lock (lists_mutex)
                    {
                        lists.Add(list.id, list);
                    }
                }

                if (!list.isValidFieldKey(var))
                {
                    ConsoleError("^b" + var + "^n has no valid list field");
                    return false;
                }


                Boolean result = list.setFieldValue(var, val, ui);

                // if delete was set, remove the limit from list
                if (Boolean.Parse(list.getField("delete")))
                {
                    ConsoleWrite("Deleting List #^b" + listId + "^n");
                    lock (lists_mutex)
                    {
                        lists.Remove(listId);
                    }
                    return false;
                }

                // check if the limit id changed
                if (result && Int32.Parse(list.id) != Int32.Parse(listId))
                {
                    if (lists.ContainsKey(list.id))
                    {
                        ConsoleError("cannot use List #^b" + list.id + "^n, already exists");
                        // set back the old limit id
                        list.setFieldValue(var, listId, ui);
                        return false;
                    }

                    // first delete the old limit entry
                    lock (lists_mutex)
                    {
                        lists.Remove(listId);
                    }
                    ConsoleWrite("Renaming List #^b" + listId + "^n to List #^b" + list.id + "^n");

                    lock (lists_mutex)
                    {
                        lists.Add(list.id, list);
                    }
                }

                return result;
            }
            finally
            {
                if (ui)
                    SaveSettings(true);
            }
        }


        public Boolean setLimitVarValue(String var, String val, Boolean ui)
        {
            try
            {


                //ConsoleWrite("Updating limit " + var + " to " + val);

                String limitId = getLimitVarId(var);
                if (limitId.Length == 0)
                    return false;

                Limit limit = null;
                if (limits.ContainsKey(limitId))
                    limit = limits[limitId];
                else
                {
                    limit = new Limit(this, limitId);
                    lock (limits_mutex)
                    {
                        limits.Add(limit.id, limit);
                    }
                }

                if (!(limit.isValidFieldKey(var) || limit.isValidGroupTitle(var)))
                {
                    ConsoleError("^b" + var + "^n has no valid field");
                    return false;
                }

                // headers do not need extra processing
                if (limit.isValidGroupTitle(var))
                    return limit.setGroupStateByTitle(var, val, ui);


                Boolean result = limit.setFieldValue(var, val, ui);

                // if delete was set, remove the limit from list
                if (Boolean.Parse(limit.getField("delete")))
                {
                    ConsoleWrite("Deleting Limit #^b" + limitId + "^n");
                    lock (limits_mutex)
                    {
                        limits.Remove(limitId);
                    }
                    return false;
                }

                // check if the limit id changed
                if (result && Int32.Parse(limit.id) != Int32.Parse(limitId))
                {
                    if (limits.ContainsKey(limit.id))
                    {
                        ConsoleError("cannot use Limit #^b" + limit.id + "^n, already exists");
                        // set back the old limit id
                        limit.setFieldValue(var, limitId, ui);
                        return false;
                    }

                    // first delete the old limit entry
                    lock (limits_mutex)
                    {
                        limits.Remove(limitId);
                    }

                    ConsoleWrite("Renaming Limit #^b" + limitId + "^n to Limit #^b" + limit.id + "^n");

                    lock (limits_mutex)
                    {
                        limits.Add(limit.id, limit);
                    }
                }

                return result;
            }
            finally
            {
                if (ui)
                    SaveSettings(true);
            }
        }


        public Boolean isIntegerVar(String var)
        {
            return this.integerVariables.ContainsKey(var);
        }

        public Int32 getIntegerVarValue(String var)
        {
            if (!isIntegerVar(var))
            {
                ConsoleError("unknown variable \"" + var + "\"");
                return -1;
            }

            return this.integerVariables[var];
        }

        public Boolean setIntegerVarValue(String var, Int32 val)
        {
            if (!isIntegerVar(var))
            {
                ConsoleError("unknown variable \"" + var + "\"");
                return false;
            }

            if (hasIntegerValidator(var))
            {
                integerVariableValidator validator = integerVarValidators[var];
                if (validator(var, val) == false)
                    return false;
            }

            this.integerVariables[var] = val;
            return true;
        }

        public Boolean hasIntegerValidator(String var)
        {
            return integerVarValidators.ContainsKey(var);
        }

        public Boolean hasBooleanValidator(String var)
        {
            return booleanVarValidators.ContainsKey(var);


        }


        public Boolean floatValidator(String var, Single value)
        {
            if (var.Equals("say_interval"))
            {
                if (!floatAssertGT(var, value, 0))
                    return false;
            }
            else if (var.Equals("update_interval"))
            {
                if (!floatAssertGTE(var, value, MIN_UPDATE_INTERVAL))
                    return false;
            }

            return true;
        }

        public Boolean integerValidator(String var, Int32 value)
        {
            if (var.Equals("delete_list"))
            {
                if (value == 0)
                    return true;

                if (!intAssertGTE(var, value, 1))
                    return false;

                try
                {
                    if (lists.ContainsKey(value.ToString()))
                    {
                        ConsoleWrite("Deleting List #^b" + value + "^n");
                        lock (lists_mutex)
                        {
                            lists.Remove(value.ToString());
                        }
                    }
                    else
                        ConsoleError("List #^b" + value + "^n does not exist");

                    return false;
                }
                finally
                {
                    SaveSettings(true);
                }
            }

            else if (var.Equals("delete_limit"))
            {
                if (value == 0)
                    return true;

                if (!intAssertGTE(var, value, 1))
                    return false;

                try
                {

                    if (limits.ContainsKey(value.ToString()))
                    {
                        ConsoleWrite("Deleting Limit #^b" + value + "^n");
                        lock (limits_mutex)
                        {
                            limits.Remove(value.ToString());
                        }
                    }
                    else
                        ConsoleError("Limit #^b" + value + "^n does not exist");

                    return false;
                }
                finally
                {
                    SaveSettings(true);
                }
            }
            else if (var.Equals("debug_level") || var.Equals("smtp_port"))
            {
                if (!intAssertGTE(var, value, 0))
                    return false;
            }
            else if (var.Equals("auto_load_interval"))
            {
                if (!intAssertGTE(var, value, 30))
                    return false;
            }
            else if (var.Equals("wait_timeout"))
            {
                if (!intAssertGTE(var, value, 30))
                    return false;
            }

            return true;
        }


        public Boolean booleanValidator(String var, Boolean value)
        {

            if (var.Equals("save_limits") && value)
            {
                Int32 count = limits.Count;
                SaveSettings(false);


                return false;
            }

            if (var.Equals("load_limits") && value)
            {
                LoadSettings(false, false);
                return false;
            }

            if (var.Equals("twitter_setup_account") && value)
            {
                SetupTwitter();
                return false;
            }

            if (var.Equals("twitter_reset_defaults") && value)
            {
                ResetTwitterDefaults();
                return false;
            }

            if (var.Equals("privacy_policy_agreement"))
            {
                if (value)
                    activate_handle.Set();
                else
                {
                    ConsoleWarn("You have not agreed to the ^bPrivacy Policy^n, disabling plugin");
                    ExecuteCommand("procon.protected.plugins.enable", this.GetType().Name, "false");
                }
                return true;
            }



            return true;
        }


        public Boolean DefaultTweet(String status)
        {
            return Tweet
                (
                status,
                default_twitter_access_token,
                default_twitter_access_token_secret,
                default_twitter_consumer_key,
                default_twitter_consumer_secret,
                true
                );
        }

        public Boolean Tweet(String status)
        {
            /* Verify that we have all the required fields */
            String access_token = getStringVarValue("twitter_access_token");
            String access_token_seceret = getStringVarValue("twitter_access_token_secret");
            String consumer_key = getStringVarValue("twitter_consumer_key");
            String consumer_secret = getStringVarValue("twitter_consumer_secret");

            return Tweet(status, access_token, access_token_seceret, consumer_key, consumer_secret, false);
        }


        public Boolean Tweet
            (
            String status,
            String access_token,
            String access_token_secret,
            String consumer_key,
            String consumer_secret,
            Boolean quiet
            )
        {
            try
            {
                if (VMode)
                {
                    ConsoleWarn("not tweeting, ^bvirtual_mode^n is ^bon^n");
                    return false;
                }

                if (String.IsNullOrEmpty(status))
                    throw new TwitterException("Cannot update Twitter status, invalid ^bstatus^n value");


                if (String.IsNullOrEmpty(access_token) || String.IsNullOrEmpty(access_token_secret) ||
                    String.IsNullOrEmpty(consumer_key) || String.IsNullOrEmpty(consumer_secret))
                    throw new TwitterException("Cannot update Twitter status, looks like you have not run Twitter setup");

                /* Create the Status Update Request */
                OAuthRequest orequest = TwitterStatusUpdateRequest(status, access_token, access_token_secret, consumer_key, consumer_secret);

                HttpWebResponse oresponse = (HttpWebResponse)orequest.request.GetResponse();

                String protcol = "HTTP/" + oresponse.ProtocolVersion + " " + (Int32)oresponse.StatusCode;

                if (!oresponse.StatusCode.Equals(HttpStatusCode.OK))
                    throw new TwitterException("Twitter UpdateStatus Request failed, " + protcol);

                if (oresponse.ContentLength == 0)
                    throw new TwitterException("Twitter UpdateStatus Request failed, ContentLength=0");

                StreamReader sin = new StreamReader(oresponse.GetResponseStream());
                String response = sin.ReadToEnd();
                sin.Close();

                Hashtable data = (Hashtable)JSON.JsonDecode(response);

                if (data == null || !data.ContainsKey("id_str"))
                    throw new TwitterException("Twitter UpdateStatus Request failed, response missing ^bid^n field");

                String id = (String)(data["id_str"].ToString());

                DebugWrite("Tweet Successful, id=^b" + id + "^n, Status: " + status, 4);

                return true;
            }
            catch (TwitterException e)
            {
                if (!quiet)
                    ConsoleException(e.Message);
            }
            catch (WebException e)
            {
                if (!quiet)
                    HandleTwitterWebException(e, "UpdateStatus");
            }
            catch (Exception e)
            {
                DumpException(e);
            }

            return false;

        }

        public void VerifyTwitterPin(String PIN)
        {
            try
            {
                if (String.IsNullOrEmpty(PIN))
                {
                    ConsoleError("Cannot verify Twitter PIN, value(^b" + PIN + "^n) is invalid");
                    return;
                }

                DebugWrite("VERIFIER_PIN: " + PIN, 5);

                hidden_variables["twitter_verifier_pin"] = true;

                if (String.IsNullOrEmpty(oauth_token) || String.IsNullOrEmpty(oauth_token_secret))
                    throw new TwitterException("Cannot verify Twitter PIN, There is no ^boauth_token^n or ^boauth_token_secret^n in memory");



                OAuthRequest orequest = TwitterAccessTokenRequest(PIN, oauth_token, oauth_token_secret);

                HttpWebResponse oresponse = (HttpWebResponse)orequest.request.GetResponse();

                String protcol = "HTTP/" + oresponse.ProtocolVersion + " " + (Int32)oresponse.StatusCode;

                if (!oresponse.StatusCode.Equals(HttpStatusCode.OK))
                    throw new TwitterException("Twitter AccessToken Request failed, " + protcol);

                if (oresponse.ContentLength == 0)
                    throw new TwitterException("Twitter AccessToken Request failed, ContentLength=0");

                StreamReader sin = new StreamReader(oresponse.GetResponseStream());
                String response = sin.ReadToEnd();

                DebugWrite("ACCESS_TOKEN_RESPONSE: " + response, 5);


                Dictionary<String, String> pairs = ParseQueryString(response);


                /* Sanity check the results */
                if (pairs.Count == 0)
                    throw new TwitterException("Twitter AccessToken Request failed, missing fields");

                /* Get the ReuestToken */
                if (!pairs.ContainsKey("oauth_token"))
                    throw new TwitterException("Twitter AccessToken Request failed, missing ^boauth_token^n field");
                oauth_token = pairs["oauth_token"];

                /* Get the RequestTokenSecret */
                if (!pairs.ContainsKey("oauth_token_secret"))
                    throw new TwitterException("Twitter AccessToken Request failed, missing ^boauth_token_secret^n field");
                oauth_token_secret = pairs["oauth_token_secret"];

                /* Get the User-Id  (Optional) */
                String user_id = String.Empty;
                if (pairs.ContainsKey("user_id"))
                    user_id = pairs["user_id"];

                /* Get the Screen-Name (Optional) */
                String screen_name = String.Empty;
                if (pairs.ContainsKey("screen_name"))
                    screen_name = pairs["screen_name"];


                ConsoleWrite("Access token, and secret obtained. Twitter setup is now complete.");
                if (!String.IsNullOrEmpty(user_id))
                    ConsoleWrite("Twitter User-Id: ^b" + user_id + "^n");
                if (!String.IsNullOrEmpty(screen_name))
                    ConsoleWrite("Twitter Screen-Name: ^b" + screen_name + "^n");

                DebugWrite("access_token=" + oauth_token, 4);
                DebugWrite("access_token_secret=" + oauth_token_secret, 4);


                setStringVarValue("twitter_access_token", oauth_token);
                setStringVarValue("twitter_access_token_secret", oauth_token_secret);
                setStringVarValue("twitter_user_id", user_id);
                setStringVarValue("twitter_screen_name", screen_name);

            }
            catch (TwitterException e)
            {
                ConsoleException(e.Message);
                ConsoleWarn("Set the field ^btwitter_setup_account^n to ^bTrue^n to re-initiate the Twitter configuration");
                return;
            }
            catch (WebException e)
            {
                HandleTwitterWebException(e, "AccessToken");
            }
            catch (Exception e)
            {
                DumpException(e);
            }


        }


        public void SetupTwitter()
        {
            try
            {
                //Display the Twitter Pin Field
                hidden_variables["twitter_verifier_pin"] = false;
                oauth_token = String.Empty;
                oauth_token_secret = String.Empty;


                OAuthRequest orequest = TwitterRequestTokenRequest();

                HttpWebResponse oresponse = (HttpWebResponse)orequest.request.GetResponse();
                String protcol = "HTTP/" + oresponse.ProtocolVersion + " " + (Int32)oresponse.StatusCode;

                if (!oresponse.StatusCode.Equals(HttpStatusCode.OK))
                    throw new TwitterException("Twitter RequestToken Request failed, " + protcol);

                if (oresponse.ContentLength == 0)
                    throw new TwitterException("Twitter RequestToken Request failed, ContentLength=0");

                StreamReader sin = new StreamReader(oresponse.GetResponseStream());
                String response = sin.ReadToEnd();

                Dictionary<String, String> pairs = ParseQueryString(response);

                if (pairs.Count == 0 || !pairs.ContainsKey("oauth_callback_confirmed"))
                    throw new TwitterException("Twitter RequestToken Request failed, missing ^boauth_callback_confirmed^n field");

                String oauth_callback_confirmed = pairs["oauth_callback_confirmed"];

                if (!oauth_callback_confirmed.ToLower().Equals("true"))
                    throw new TwitterException("Twitter RequestToken Request failed, ^boauth_callback_confirmed^n=^b" + oauth_callback_confirmed + "^n");

                /* Get the ReuestToken */
                if (!pairs.ContainsKey("oauth_token"))
                    throw new TwitterException("Twitter RequestToken Request failed, missing ^boauth_token^n field");
                oauth_token = pairs["oauth_token"];

                /* Get the RequestTokenSecret */
                if (!pairs.ContainsKey("oauth_token_secret"))
                    throw new TwitterException("Twitter RequestToken Request failed, missing ^boauth_token_secret^n field");
                oauth_token_secret = pairs["oauth_token_secret"];



                DebugWrite("REQUEST_TOKEN_RESPONSE: " + response, 5);
                DebugWrite("oauth_callback_confirmed=" + oauth_callback_confirmed, 4);
                DebugWrite("oauth_token=" + oauth_token, 4);
                DebugWrite("oauth_token_secret=" + oauth_token_secret, 4);

                ConsoleWrite("Please visit the following site to obtain the ^btwitter_verifier_pin^n");
                ConsoleWrite("http://api.twitter.com/oauth/authorize?oauth_token=" + oauth_token);


            }
            catch (TwitterException e)
            {
                ConsoleException(e.Message);
                return;
            }
            catch (WebException e)
            {
                HandleTwitterWebException(e, "RequestToken");
            }
            catch (Exception e)
            {
                DumpException(e);
            }

        }

        public void HandleTwitterWebException(WebException e, String prefix)
        {
            HttpWebResponse response = (HttpWebResponse)e.Response;
            String protcol = (response == null) ? "" : "HTTP/" + response.ProtocolVersion;

            String error = String.Empty;
            //try reading JSON response
            if (response != null && response.ContentType != null && response.ContentType.ToLower().Contains("json"))
            {
                try
                {
                    StreamReader sin = new StreamReader(response.GetResponseStream());
                    String data = sin.ReadToEnd();
                    sin.Close();

                    Hashtable jdata = (Hashtable)JSON.JsonDecode(data);
                    if (jdata == null || !jdata.ContainsKey("error") ||
                        jdata["error"] == null || !jdata["error"].GetType().Equals(typeof(String)))
                        throw new Exception();

                    error = "Twitter Error: " + (String)jdata["error"] + ", ";
                }
                catch (Exception)
                {
                }
            }

            /* Handle Time-Out Gracefully */
            if (e.Status.Equals(WebExceptionStatus.Timeout))
            {
                ConsoleException("Twitter " + prefix + " Request(" + protcol + ") timed-out");
                return;
            }
            else if (e.Status.Equals(WebExceptionStatus.ProtocolError))
            {
                ConsoleException("Twitter " + prefix + " Request(" + protcol + ") failed, " + error + " " + e.GetType() + ": " + e.Message);
                return;
            }
            else
                throw e;
        }

        public Dictionary<String, String> ParseQueryString(String text)
        {

            MatchCollection matches = Regex.Matches(text, @"([^=]+)=([^&]+)&?", RegexOptions.IgnoreCase);

            Dictionary<String, String> pairs = new Dictionary<String, String>();

            foreach (Match match in matches)
                if (match.Success && !pairs.ContainsKey(match.Groups[1].Value))
                    pairs.Add(match.Groups[1].Value, match.Groups[2].Value);

            return pairs;
        }


        public static Int32 MAX_STATUS_LENGTH = 140;
        public OAuthRequest TwitterStatusUpdateRequest(
            String status,
            String access_token,
            String access_token_secret,
            String consumer_key,
            String consumer_secret)
        {
            System.Net.ServicePointManager.Expect100Continue = false;

            if (String.IsNullOrEmpty(status))
                return null;


            String suffix = "...";
            if (status.Length > MAX_STATUS_LENGTH)
                status = status.Substring(0, MAX_STATUS_LENGTH - suffix.Length) + suffix;


            OAuthRequest orequest = new OAuthRequest(this, "https://api.twitter.com/1.1/statuses/update.json"); // Fix #48
            orequest.Method = HTTPMethod.POST;
            orequest.request.ContentType = "application/x-www-form-urlencoded";

            /* Set the Post Data */

            Byte[] data = Encoding.UTF8.GetBytes("status=" + OAuthRequest.UrlEncode(Encoding.UTF8.GetBytes(status)));

            // Parameters required by the Twitter OAuth Protocol
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_consumer_key", consumer_key));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_nonce", Guid.NewGuid().ToString("N")));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_signature_method", "HMAC-SHA1"));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_token", access_token));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_timestamp", ((Int64)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds).ToString()));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_version", "1.0"));
            orequest.parameters.Add(new KeyValuePair<String, String>("status", OAuthRequest.UrlEncode(Encoding.UTF8.GetBytes(status))));

            // Compute and add the signature
            String signature = orequest.Signature(consumer_secret, access_token_secret);
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_signature", OAuthRequest.UrlEncode(signature)));

            // Add the OAuth authentication header
            String OAuthHeader = orequest.Header();
            orequest.request.AuthenticationLevel = System.Net.Security.AuthenticationLevel.MutualAuthRequired;
            orequest.request.Headers["Authorization"] = OAuthHeader;

            // Add the POST body
            orequest.request.ContentLength = data.Length;
            Stream sout = orequest.request.GetRequestStream();
            sout.Write(data, 0, data.Length);
            sout.Close();

            return orequest;
        }


        public OAuthRequest TwitterAccessTokenRequest(String verifier, String token, String secret)
        {
            OAuthRequest orequest = new OAuthRequest(this, "https://api.twitter.com/oauth/access_token"); // Fix #48
            orequest.Method = HTTPMethod.POST;
            orequest.request.ContentLength = 0;

            // Parameters required by the Twitter OAuth Protocol
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_consumer_key", getStringVarValue("twitter_consumer_key")));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_nonce", Guid.NewGuid().ToString("N")));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_timestamp", ((Int64)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds).ToString()));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_signature_method", "HMAC-SHA1"));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_version", "1.0"));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_token", token));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_verifier", verifier));

            // Compute and add the signature
            String signature = orequest.Signature(getStringVarValue("twitter_consumer_secret"), secret);
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_signature", OAuthRequest.UrlEncode(signature)));

            // Add the OAuth authentication header
            String OAuthHeader = orequest.Header();
            orequest.request.AuthenticationLevel = System.Net.Security.AuthenticationLevel.MutualAuthRequired;
            orequest.request.Headers["Authorization"] = OAuthHeader;



            return orequest;
        }

        public OAuthRequest TwitterRequestTokenRequest()
        {
            OAuthRequest orequest = new OAuthRequest(this, "https://api.twitter.com/oauth/request_token"); // Fix #48
            orequest.Method = HTTPMethod.POST;
            orequest.request.ContentLength = 0;

            // Parameters required by the Twitter OAuth Protocol
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_callback", OAuthRequest.UrlEncode("oob")));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_consumer_key", getStringVarValue("twitter_consumer_key")));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_nonce", Guid.NewGuid().ToString("N")));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_timestamp", ((Int64)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds).ToString()));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_signature_method", "HMAC-SHA1"));
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_version", "1.0"));

            // Compute and add the signature
            String signature = orequest.Signature(getStringVarValue("twitter_consumer_secret"), null);
            orequest.parameters.Add(new KeyValuePair<String, String>("oauth_signature", OAuthRequest.UrlEncode(signature)));

            // Add the OAuth authentication header
            String OAuthHeader = orequest.Header();
            orequest.request.AuthenticationLevel = System.Net.Security.AuthenticationLevel.MutualAuthRequired;
            orequest.request.Headers["Authorization"] = OAuthHeader;

            return orequest;
        }


        public Boolean intAssertGTE(String var, Int32 value, Int32 min_value)
        {
            if (!(value >= min_value))
            {
                ConsoleError("^b" + var + "(^n" + value + "^b)^n must be greater than or equal to ^b" + min_value + "^n^0");
                return false;
            }

            return true;
        }

        public Boolean floatAssertGTE(String var, Single value, Single min_value)
        {
            if (!(value >= min_value))
            {
                ConsoleError("^b" + var + "(^n" + value + "^b)^n must be greater than or equal to ^b" + min_value + "^n^0");
                return false;
            }

            return true;
        }

        private Boolean floatAssertGT(String var, Single value, Single min_value)
        {
            if (!(value > min_value))
            {
                ConsoleError("^b" + var + "(^n" + value + "^b)^n must be greater than  ^b" + min_value + "^n^0");
                return false;
            }

            return true;
        }


        private Boolean hasStringValidator(String var)
        {
            return stringVarValidators.ContainsKey(var);
        }

        private Boolean isStringVar(String var)
        {
            return this.stringVariables.ContainsKey(var);
        }

        public String getStringVarValue(String var)
        {
            if (!isStringVar(var))
            {
                ConsoleError("unknown variable \"" + var + "\"");
                return "";
            }

            return this.stringVariables[var];
        }

        public Boolean SaveSettings(Boolean quiet)
        {

            String file = getStringVarValue("limits_file");

            try
            {
                lock (settings_mutex)
                {
                    String settings = "";
                    String cname = this.GetType().Name;

                    //save the exported variables first
                    foreach (String ckey in exported_variables)
                    {
                        String cvalue = getBooleanVarValue(ckey).ToString();
                        settings += "procon.protected.plugins.setVariable \"" + cname + "\" \"" + ckey + "\" \"BASE64:" + Encode(cvalue) + "\"" + NL;
                    }

                    List<String> keys = new List<String>(limits.Keys);
                    foreach (String key in keys)
                    {
                        if (!limits.ContainsKey(key))
                            continue;

                        Limit limit = limits[key];

                        if (limit.Enabled)
                        {
                            DebugWrite("^8^b============= In SaveSettings " + limit.id, 9);
                            limit.ResetLastInterval(DateTime.Now);
                        }

                        Dictionary<String, String> lsettings = limit.getSettings(false);
                        foreach (KeyValuePair<String, String> pair in lsettings)
                            settings += "procon.protected.plugins.setVariable \"" + cname + "\" \"" + pair.Key + "\" \"BASE64:" + Encode(pair.Value) + "\"" + NL;
                    }


                    keys = new List<String>(lists.Keys);
                    foreach (String key in keys)
                    {
                        if (!lists.ContainsKey(key))
                            continue;

                        CustomList list = lists[key];
                        Dictionary<String, String> lsettings = list.getSettings(false);
                        foreach (KeyValuePair<String, String> pair in lsettings)
                            settings += "procon.protected.plugins.setVariable \"" + cname + "\" \"" + pair.Key + "\" \"BASE64:" + Encode(pair.Value) + "\"" + NL;
                    }


                    if (!File.Exists(file) && !quiet)
                        ConsoleWarn("file ^b" + file + "^n does not exist, will create new one");


                    DumpData(settings, file);

                    Int32 lmcount = limits.Count;
                    Int32 lscount = lists.Count;

                    if (!quiet || getIntegerVarValue("debug_level") >= 5)
                        ConsoleWrite(lmcount + " limit" + ((lmcount > 1 || lmcount == 0) ? "s" : "") + " and " + lscount + " list" + ((lscount > 1 || lscount == 0) ? "s" : "") + " saved to ^b" + file + "^n");
                }

            }
            catch (Exception e)
            {
                DumpException(e);
                return false;
            }

            return true;
        }




        public Boolean LoadSettings(Boolean force, Boolean quiet)
        {
            return LoadSettings(force, true, quiet);
        }

        public Boolean LoadSettings(Boolean force, Boolean compile, Boolean quiet)
        {
            try
            {
                lock (settings_mutex)
                {
                    String file = getStringVarValue("limits_file");

                    Boolean exist = File.Exists(file);

                    if (!exist && force)
                        File.Create(file, 1024, FileOptions.None).Close();
                    else if (!exist)
                    {
                        ConsoleError("file ^b" + file + "^n does not exist");
                        return false;
                    }

                    String[] lines = File.ReadAllLines(file);
                    Int32 lscount = 0;
                    Int32 lmcount = 0;
                    foreach (String line in lines)
                    {

                        String ln = line.Trim();
                        if (ln.Length == 0 || ln.StartsWith("#") || ln.StartsWith(";"))
                            continue;

                        MatchCollection collection = Regex.Matches(ln, "\"([^\"]+)\"", RegexOptions.IgnoreCase);

                        if (collection.Count != 3)
                            continue;


                        String var = collection[1].Groups[1].Value;
                        String value = collection[2].Groups[1].Value;

                        if (Regex.Match(var, @"limit_\d+_id", RegexOptions.IgnoreCase).Success)
                            lmcount++;

                        if (Regex.Match(var, @"list_\d+_id", RegexOptions.IgnoreCase).Success)
                            lscount++;

                        SetPluginVariable(var, value);

                    }

                    if (!quiet || getIntegerVarValue("debug_level") >= 5)
                        ConsoleWrite(lmcount + " limit" + ((lmcount > 1 || lmcount == 0) ? "s" : "") + " and " + lscount + " list" + ((lscount > 1 || lscount == 0) ? "s" : "") + " loaded from ^b" + file + "^n");

                    CompileAll();

                    activate_handle.Set();

                }

            }
            catch (Exception e)
            {
                DumpException(e);
                return false;
            }

            return true;

        }

        public String FixLimitsFilePath()
        {
            String file = getStringVarValue("limits_file");
            try
            {
                if (Path.GetFileNameWithoutExtension(file).Equals(this.GetType().Name))
                    file = makeRelativePath(this.GetType().Name + "_" + server_host + "_" + server_port + ".conf");

            }
            catch (Exception e)
            {
                DumpException(e);
            }

            return file.Trim();

        }

        private Boolean setStringVarValue(String var, String val)
        {
            if (!isStringVar(var))
            {
                ConsoleError("unknown variable \"" + var + "\"");
                return false;
            }


            if (hasStringValidator(var))
            {
                stringVariableValidator validator = stringVarValidators[var];
                if (validator(var, val) == false)
                    return false;
            }



            this.stringVariables[var] = val;


            if (var.Equals("limits_file"))
                this.stringVariables[var] = FixLimitsFilePath();



            return true;
        }



        private Boolean isStringListVar(String var)
        {
            return this.stringListVariables.ContainsKey(var);
        }

        private List<String> getStringListVarValue(String var)
        {
            if (!isStringListVar(var))
            {
                ConsoleError("variable \"" + var + "\"");
                return new List<String>();
            }

            String[] out_list = Regex.Split(this.stringListVariables[var].Replace(";", ",").Replace("|", ","), @"\s*,\s*");
            return new List<String>(out_list);
        }

        private Boolean setStringListVarValue(String var, List<String> val)
        {
            if (!isStringListVar(var))
            {
                ConsoleError("^1^bERROR^0^n: unknown variable \"" + var + "\"");
                return false;
            }

            List<String> cleanList = new List<String>();
            foreach (String item in val)
                if (Regex.Match(item, @"^\s*$").Success)
                    continue;
                else
                    cleanList.Add(item);

            this.stringListVariables[var] = String.Join("|", cleanList.ToArray());
            return true;
        }

        public Boolean isFloatVar(String var)
        {
            return this.floatVariables.ContainsKey(var);
        }

        public Single getFloatVarValue(String var)
        {
            if (!isFloatVar(var))
            {
                ConsoleError("unknown variable 3 ^b" + var);
                return -1F;
            }

            return this.floatVariables[var];
        }

        public Boolean setFloatVarValue(String var, Single val)
        {
            if (!isFloatVar(var))
            {
                ConsoleError("unknown variable 4 ^b" + var);
                return false;
            }

            if (hasFloatValidator(var))
            {
                floatVariableValidator validator = floatVarValidators[var];
                if (validator(var, val) == false)
                    return false;
            }

            this.floatVariables[var] = val;
            return true;
        }

        public Boolean hasFloatValidator(String var)
        {
            return floatVarValidators.ContainsKey(var);
        }


        public Boolean isBooleanVar(String var)
        {
            return this.booleanVariables.ContainsKey(var);
        }

        public Boolean getBooleanVarValue(String var)
        {
            if (!isBooleanVar(var))
            {
                ConsoleError("unknown variable 5 ^b" + var);
                return false;
            }

            return this.booleanVariables[var];
        }

        public Boolean setBooleanVarValue(String var, Boolean val)
        {
            if (!isBooleanVar(var))
            {
                ConsoleError("unknown variable 6 ^b" + var);
                return false;
            }


            if (hasBooleanValidator(var))
            {
                booleanVariableValidator validator = booleanVarValidators[var];
                if (validator(var, val) == false)
                    return false;
            }

            this.booleanVariables[var] = val;

            return true;
        }

        public Boolean isPluginVar(String var)
        {
            return getPluginVars().Contains(var) || Limit.isLimitVar(var) || CustomList.isListVar(var);
        }


        public String getPluginVarValue(String var)
        {
            return getPluginVarValue(null, var);
        }

        public String getPluginVarValue(String sender, String var)
        {

            if (var == "rcon_to_battlelog_codes")
            {
                String condensed = String.Empty;
                foreach (String k in rcon2bw_user.Keys)
                {
                    if (String.IsNullOrEmpty(condensed))
                    {
                        condensed = k + "=" + rcon2bw_user[k];
                    }
                    else
                    {
                        condensed = condensed + ", " + k + "=" + rcon2bw_user[k];
                    }
                }
                return condensed;
            }

            if (!isPluginVar(var))
            {
                SendConsoleError(sender, "unknown variable ^b" + var);
                return "";
            }

            if (isFloatVar(var))
                return getFloatVarValue(var).ToString();
            else if (isBooleanVar(var))
                return getBooleanVarValue(var).ToString();
            else if (isIntegerVar(var))
                return getIntegerVarValue(var).ToString();
            else if (Limit.isLimitVar(var))
                return getLimitVarValue(var).ToString();
            else if (CustomList.isListVar(var))
                return getListVarValue(var).ToString();
            else if (isStringListVar(var))
                return String.Join(", ", getStringListVarValue(var).ToArray());
            else if (isStringVar(var))
                return getStringVarValue(var);
            else
            {
                SendConsoleError(sender, "unknown variable ^b" + var);
                return "";
            }
        }

        public List<String> getPluginVars()
        {
            return getPluginVars(true, true, true, false);
        }

        public List<String> getPluginVars(Boolean include_limits, Boolean include_lists, Boolean display)
        {
            return getPluginVars(include_limits, include_lists, true, display);
        }



        public List<String> getPluginVars(Boolean include_limits, Boolean include_lists, Boolean include_vars, Boolean display)
        {
            List<String> vars = new List<String>();

            vars.AddRange(getBooleanPluginVars());
            vars.AddRange(getFloatPluginVars());
            vars.AddRange(getIntegerPluginVars());
            vars.AddRange(getStringListPluginVars());
            vars.AddRange(getStringPluginVars());
            vars.Add("rcon_to_battlelog_codes");

            if (include_lists)
            {
                List<String> keys = new List<String>();

                lock (lists_mutex) { keys.AddRange(lists.Keys); }

                for (Int32 i = 0; i < keys.Count; i++)
                {
                    String key = keys[i];

                    CustomList list = null;

                    if (!lists.TryGetValue(key, out list))
                        continue;

                    vars.AddRange(list.getSettings(display).Keys);
                }
            }

            if (include_limits)
            {
                List<String> keys = new List<String>();

                lock (limits_mutex) { keys.AddRange(limits.Keys); }

                for (Int32 i = 0; i < keys.Count; i++)
                {
                    String key = keys[i];

                    Limit limit = null;
                    if (!limits.TryGetValue(key, out limit))
                        continue;

                    vars.AddRange(limit.getSettings(display).Keys);
                }
            }

            if (!include_vars)
            {
                foreach (String var in exported_variables)
                {
                    if (vars.Contains(var))
                        vars.Remove(var);
                }
            }



            return vars;
        }

        public List<String> getFloatPluginVars()
        {
            return new List<String>(this.floatVariables.Keys);
        }

        public List<String> getBooleanPluginVars()
        {
            return new List<String>(this.booleanVariables.Keys);
        }

        public List<String> getIntegerPluginVars()
        {
            return new List<String>(this.integerVariables.Keys);
        }

        private List<String> getStringListPluginVars()
        {
            return new List<String>(this.stringListVariables.Keys);
        }

        private List<String> getStringPluginVars()
        {
            return new List<String>(this.stringVariables.Keys);
        }

        public void DumpData(String s)
        {
            // Create a temporary file
            String path = Path.GetRandomFileName() + ".dump";
            ConsoleWrite("^1Dumping information in file " + path);
            DumpData(s, path);
        }

        public void DumpData(String s, String path)
        {
            try
            {
                FileMode mode = FileMode.CreateNew;
                if (File.Exists(path))
                    mode = FileMode.Truncate;
                //File.Delete(path);

                using (FileStream fs = File.Open(path, mode, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    Byte[] info = new UTF8Encoding(true).GetBytes(s);
                    fs.Write(info, 0, info.Length);
                }
            }
            catch (Exception ex)
            {
                ConsoleError("unable to dump information to file");
                ConsoleException("" + ex.GetType() + ": " + ex.Message);
            }
        }

        public void AppendData(String s, String path)
        {
            try
            {
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(Directory.GetParent(Environment.ProcessPath).FullName, path);


                using (FileStream fs = File.Open(path, FileMode.Append))
                {
                    Byte[] info = new UTF8Encoding(true).GetBytes(s);
                    fs.Write(info, 0, info.Length);
                }
            }
            catch (Exception ex)
            {
                ConsoleError("unable to append data to file " + path + "");
                ConsoleException("" + ex.GetType() + ": " + ex.Message);
            }
        }

        public void DumpException(Exception e)
        {
            DumpException(e, String.Empty);
        }

        public void DumpException(Exception e, String prefix)
        {
            Int32 debug_level = getIntegerVarValue("debug_level");

            try
            {
                String class_name = this.GetType().Name;
                String path = class_name + ".dump";


                if (prefix == null)
                    prefix = String.Empty;
                else
                    prefix += ": ";


                if (e.GetType().Equals(typeof(ThreadAbortException)))
                {
                    Thread.ResetAbort();
                    return;
                }
                else if (e.GetType().Equals(typeof(TargetInvocationException)) && e.InnerException != null)
                {
                    if (debug_level >= 4) ConsoleException(prefix + e.InnerException.GetType() + ": " + e.InnerException.Message);
                    DebugWrite("^1Extra information dumped in file " + path, 4);
                    DumpExceptionFile(e, path);
                    DumpExceptionFile(e.InnerException, path);
                }
                else
                {
                    if (debug_level >= 4) ConsoleException(prefix + e.GetType() + ": " + e.Message);

                    foreach (DictionaryEntry de in e.Data)
                    {
                        DebugWrite("    " + de.Key.ToString() + ": " + de.Value.ToString(), 4);
                    }

                    DebugWrite("^1Extra information dumped in file " + path, 4);
                    DumpExceptionFile(e, path);
                }
            }
            catch (Exception ex)
            {
                if (debug_level >= 4) ConsoleWarn("unable to dump extra exception information.");
                if (debug_level >= 4) ConsoleException(ex.GetType() + ": " + ex.Message);
            }
        }

        public void DumpExceptionFile(Exception e, String path)
        {
            DumpExceptionFile(e, path, String.Empty);
        }

        public void DumpExceptionFile(Exception e, String path, String extra)
        {
            String class_name = this.GetType().Name;

            using (FileStream fs = File.Open(path, FileMode.Append))
            {
                String version = GetPluginVersion();
                String trace_str = "\n-----------------------------------------------\n";
                trace_str += "Version: " + class_name + " " + version + "\n";
                trace_str += "Date: " + DateTime.Now.ToString() + "\n";

                if (!(extra == null && extra.Length == 0))
                    trace_str += "Data: " + extra + "\n";

                trace_str += e.GetType() + ": " + e.Message + "\n\n";
                trace_str += "Stack Trace: \n" + e.StackTrace + "\n\n";
                trace_str += "MSIL Stack Trace:\n";


                StackTrace trace = new StackTrace(e);
                StackFrame[] frames = trace.GetFrames();
                foreach (StackFrame frame in frames)
                    trace_str += "    " + frame.GetMethod() + ", IL: " + String.Format("0x{0:X}", frame.GetILOffset()) + "\n";


                Byte[] info = new UTF8Encoding(true).GetBytes(trace_str);
                fs.Write(info, 0, info.Length);
            }

        }

        public enum MessageType { Warning, Error, Exception, Normal };


        public String FormatMessage(String msg, MessageType type)
        {
            String prefix = "[^b" + GetPluginName() + "^n] ";

            if (Thread.CurrentThread.Name != null)
                prefix += "Thread(^b" + Thread.CurrentThread.Name + "^n): ";

            if (type.Equals(MessageType.Warning))
                prefix += "^1^bWARNING^0^n: ";
            else if (type.Equals(MessageType.Error))
                prefix += "^1^bERROR^0^n: ";
            else if (type.Equals(MessageType.Exception))
                prefix += "^1^bEXCEPTION^0^n: ";

            return prefix + msg.Replace('{', '(').Replace('}', ')');
        }


        public void LogWrite(String msg)
        {
            this.ExecuteCommand("procon.protected.pluginconsole.write", msg);
        }

        public void ConsoleWrite(String msg, MessageType type)
        {
            LogWrite(FormatMessage(msg, type));
        }

        public void ConsoleWrite(String msg)
        {
            ConsoleWrite(msg, MessageType.Normal);
        }

        public void ConsoleWarn(String msg)
        {
            ConsoleWrite(msg, MessageType.Warning);
        }

        public void ConsoleError(String msg)
        {
            ConsoleWrite(msg, MessageType.Error);
        }

        public void ConsoleException(String msg)
        {
            ConsoleWrite(msg, MessageType.Exception);
        }

        public void DebugWrite(String msg, Int32 level)
        {
            if (getIntegerVarValue("debug_level") >= level)
                ConsoleWrite(msg, MessageType.Normal);
        }



        public void ServerCommand(params String[] args)
        {
            List<String> list = new List<String>();
            list.Add("procon.protected.send");
            list.AddRange(args);
            this.ExecuteCommand(list.ToArray());
        }

        public void PunkBusterCommand(String text)
        {
            ServerCommand("punkBuster.pb_sv_command", text);
        }

        public Boolean PRoConChat(String text)
        {
            if (VMode)
            {
                ConsoleWarn("not sending procon chat \"" + text + "\", ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            this.ExecuteCommand("procon.protected.chat.write", E(text));

            return true;
        }

        public Boolean PRoConEvent(String text, String player)
        {
            return PRoConEvent(EventType.Plugins, CapturableEvent.PluginAction, text, player);
        }

        public Boolean PRoConEvent(EventType type, CapturableEvent name, String text, String player)
        {
            if (VMode)
            {
                ConsoleWarn("not sending procon event(^b" + type.ToString() + "^n:^b" + name.ToString() + "^n:^b" + player + "^n) \"" + text + "\", ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            this.ExecuteCommand("procon.protected.events.write", type.ToString(), name.ToString(), text, player);

            return true;
        }

        public Boolean SendMail(String address, String subject, String body)
        {
            if (VMode)
            {
                ConsoleWarn("not sending email, ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            //send mail in a separate thread to avoid halting ProCon if SMTP request times out
            Thread mail_thread = new Thread(new ThreadStart(delegate ()
            {
                try
                {
                    String smtp_host = getStringVarValue("smtp_host");
                    String smtp_account = getStringVarValue("smtp_account");
                    String smtp_mail = getStringVarValue("smtp_mail");
                    String smtp_password = getStringVarValue("smtp_password");
                    Int32 smtp_port = getIntegerVarValue("smtp_port");
                    Boolean smtp_ssl = getBooleanVarValue("smtp_ssl");

                    MailMessage message = new MailMessage();

                    //split at the commas to allow multiple addresses
                    List<String> address_list = new List<String>(address.Split(','));
                    address_list.RemoveAll(delegate (String i) { return i == null || i.Trim().Length == 0; });

                    foreach (String addrs in address_list)
                        message.To.Add(addrs.Trim());

                    message.Subject = subject;
                    message.From = new MailAddress(smtp_mail);
                    message.Body = body;
                    SmtpClient smtp = new SmtpClient(smtp_host, smtp_port);
                    smtp.EnableSsl = smtp_ssl;
                    smtp.Credentials = new NetworkCredential(smtp_account, smtp_password);
                    smtp.Send(message);
                }
                catch (Exception e)
                {
                    DumpException(e);
                }
            }));

            mail_thread.IsBackground = true;
            mail_thread.Name = "mailer";
            mail_thread.Start();

            return true;
        }

        public Boolean SendSMS(String country, String carrier, String number, String message)
        {
            if (VMode)
            {
                ConsoleWarn("not sending SMS, ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            if (country == null || carrier == null || message == null || number == null)
                return false;

            country = country.Trim();
            carrier = carrier.Trim();
            number = number.Trim();

            if (country.Length == 0)
                throw new EvaluateException(FormatMessage("SMS country is empty", MessageType.Error));

            if (carrier.Length == 0)
                throw new EvaluateException(FormatMessage("SMS carrier is empty", MessageType.Error));

            if (number.Length == 0)
                throw new EvaluateException(FormatMessage("SMS number is empty", MessageType.Error));

            if (!CarriersDict.ContainsKey(country) || CarriersDict[country] == null)
                throw new EvaluateException(FormatMessage("uknown SMS country ^b" + country + "^n", MessageType.Error));

            number = Regex.Replace(number, @"[^+0-9]", "");

            if (number.Length == 0)
                throw new EvaluateException(FormatMessage("SMS number is empty after removing non-numeric characters", MessageType.Error));

            Dictionary<String, String> gateways = CarriersDict[country];

            if (!gateways.ContainsKey(carrier) || gateways[carrier] == null)
                throw new EvaluateException(FormatMessage("uknown SMS Gateway for carrier ^b" + carrier + "^n", MessageType.Error));

            String gateway = gateways[carrier];

            gateway = Regex.Replace(gateway, "number", number, RegexOptions.IgnoreCase);

            return SendMail(gateway, "Limit Activation", message);

        }

        public Boolean SendTaskbarNotification(String title, String message)
        {
            if (VMode)
            {
                ConsoleWarn("not sending taskbar notification, ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            //ExecuteCommand("procon.protected.playsound", title, message);
            ExecuteCommand("procon.protected.notification.write", title, message);
            return true;
        }

        public Boolean SendSoundNotification(String soundfile, String soundfilerepeat)
        {
            if (VMode)
            {
                ConsoleWarn("not sending sound notification, ^bvirtual_mode^n is ^bon^n");
                return false;
            }
            ExecuteCommand("procon.protected.playsound", soundfile, soundfilerepeat);
            //ExecuteCommand("procon.protected.notification.write", title, message);
            return true;
        }

        public String FriendlySpan(TimeSpan span)
        {
            String formatted = String.Format("{0}{1}{2}{3}",
                span.Days > 0 ? String.Format("{0:0} days, ", span.Days) : String.Empty,
                span.Hours > 0 ? String.Format("{0:0} hours, ", span.Hours) : String.Empty,
                span.Minutes > 0 ? String.Format("{0:0} minutes, ", span.Minutes) : String.Empty,
                span.Seconds > 0 ? String.Format("{0:0} seconds", span.Seconds) : String.Empty);

            if (formatted.EndsWith(", ")) formatted = formatted.Substring(0, formatted.Length - 2);

            return formatted;
        }


        public String FriendlyMapName(String mapFileName)
        {
            if (String.IsNullOrEmpty(mapFileName)) return String.Empty;
            String ret = mapFileName;
            if (friendlyMaps.ContainsKey(mapFileName)) ret = friendlyMaps[mapFileName];
            return ret;
        }

        public String FriendlyModeName(String modeName)
        {
            if (String.IsNullOrEmpty(modeName)) return String.Empty;
            String ret = modeName;
            if (friendlyModes.ContainsKey(modeName)) ret = friendlyModes[modeName];
            return ret;
        }

        private String GetCategory(Kill info)
        {
            DamageTypes category = DamageTypes.None;

            if (info == null || String.IsNullOrEmpty(info.DamageType))
                return "None";

            if (!WeaponsDict.TryGetValue(info.DamageType, out category))
            {
                category = DamageTypes.None;
            }

            return category.ToString();
        }

        public KillReasonInterface FriendlyWeaponName(String killWeapon)
        {
            KillReason r = new KillReason();
            r._name = killWeapon;
            DamageTypes category = DamageTypes.None;
            Boolean hasCategory = false;

            if (WeaponsDict.TryGetValue(killWeapon, out category))
            {
                hasCategory = true;
            }

            if (game_version == "BF3")
            {
                Match m = Regex.Match(killWeapon, @"/([^/]+)$");
                r._name = killWeapon;
                if (m.Success) r._name = m.Groups[1].Value;
            }
            else if (killWeapon.StartsWith("U_")) // BF4 weapons // TBD BFH
            {
                String[] tParts = killWeapon.Split(new[] { '_' });

                if (tParts.Length == 2)
                { // U_Name
                    r._name = tParts[1];
                }
                else if (tParts.Length == 3)
                { // U_Name_Detail
                    r._name = tParts[1];
                    r._detail = tParts[2];
                }
                else if (tParts.Length >= 4)
                { // U_AttachedTo_Name_Detail
                    r._name = tParts[2];
                    r._detail = tParts[3];
                    r._attachedTo = tParts[1];
                }
                else
                {
                    DebugWrite("Warning: unrecognized weapon code: " + killWeapon, 5);
                }
            }
            else if (killWeapon != "Death" && hasCategory) // BF4 vehicles?
            {
                if (category == DamageTypes.VehicleAir
                || category == DamageTypes.VehicleHeavy
                || category == DamageTypes.VehicleLight
                || category == DamageTypes.VehiclePersonal
                || category == DamageTypes.VehicleStationary
                || category == DamageTypes.VehicleTransport
                || category == DamageTypes.VehicleWater)
                {
                    r._name = "Death";
                    r._vName = killWeapon;
                    Match m = Regex.Match(killWeapon, @"/([^/]+)/([^/]+)$");
                    if (m.Success)
                    {
                        r._vName = m.Groups[1].Value;
                        r._vDetail = m.Groups[2].Value;
                    }

                    // Clean-up heuristics
                    String vn = r._vName;
                    if (vn.StartsWith("CH_"))
                        vn = vn.Replace("CH_", String.Empty);
                    else if (vn.StartsWith("Ch_"))
                        vn = vn.Replace("Ch_", String.Empty);
                    else if (vn.StartsWith("RU_"))
                        vn = vn.Replace("RU_", String.Empty);
                    else if (vn.StartsWith("US_"))
                        vn = vn.Replace("US_", String.Empty);

                    if (vn == "spec" && r._vDetail != null)
                    {
                        if (r._vDetail.Contains("Z-11w"))
                            vn = "Z-11w";
                        else if (r._vDetail.Contains("DV15"))
                            vn = "DV15";
                        else vn = r._vDetail;
                    }

                    if (vn.StartsWith("FAC_"))
                        vn = vn.Replace("FAC_", "Boat ");
                    else if (vn.StartsWith("FAC-"))
                        vn = vn.Replace("FAC-", "Boat ");
                    else if (vn.StartsWith("JET_"))
                        vn = vn.Replace("JET_", "Jet ");
                    else if (vn.StartsWith("FJET_"))
                        vn = vn.Replace("FJET_", "Jet ");

                    if (vn == "LAV25" && r._vDetail != null)
                    {
                        if (r._vDetail == "LAV_AD")
                        {
                            vn = "AA LAV_AD";
                        }
                        else
                        {
                            vn = "IFV LAV25";
                        }
                    }

                    switch (vn)
                    {
                        case "9K22_Tunguska_M": vn = "AA Tunguska"; break;
                        case "AC130": vn = "AC130 Gunship"; break;
                        case "AH1Z": vn = "Chopper AH1Z Viper"; break;
                        case "AH6": vn = "Chopper AH6 Littlebird"; break;
                        case "BTR-90": vn = "IFV BTR-90"; break;
                        case "F35": vn = "Jet F35"; break;
                        case "HIMARS": vn = "Artillery Truck M142 HIMARS"; break;
                        case "M1A2": vn = "MBT M1A2"; break;
                        case "Mi28": vn = "Chopper Mi28 Havoc"; break;
                        case "SU-25TM": vn = "Jet SU-25TM"; break;
                        case "Venom": vn = "Chopper Venom"; break;
                        case "Z-11w": vn = "Chopper Z-11w"; break;
                        case "KLR650": vn = "Bike KLR650"; break;
                        case "DPV": vn = "Jeep DPV"; break;
                        case "LTHE_Z-9": vn = "Chopper Z-9"; break;
                        case "FAV_LYT2021": vn = "Jeep LYT2021"; break;
                        case "GrowlerITV": vn = "Jeep Growler ITV"; break;
                        case "Ka-60": vn = "Chopper Ka-60"; break;
                        case "VDV Buggy": vn = "Jeep VDV Buggy"; break;
                        case "T90": vn = "MBT T90"; break;
                        case "A-10_THUNDERBOLT": vn = "Jet A-10 Thunderbolt"; break;
                        case "B1Lancer": vn = "Jet B1 Lancer"; break;
                        case "H6K": vn = "Jet H6K"; break;
                        case "Z-10w": vn = "Chopper Z-10w"; break;
                        case "RHIB": vn = "Boat RHIB"; break;
                        default: break;
                    }

                    r._vName = vn.Replace('_', ' ');
                }
            }
            return r;
        }

        public Boolean Log(String file, String message)
        {
            AppendData(StripModifiers(E(message) + NL), file);

            return true;
        }

        public Boolean KillPlayer(String name, Int32 delay)
        {
            Boolean cVmode = (Boolean)Thread.GetData(VModeSlot);

            Thread delayed_kill = new Thread(new ThreadStart(delegate ()
            {
                if (VModeSlot == null)
                    VModeSlot = Thread.AllocateDataSlot();

                // propagate the per-limit virtual mode to child thread
                Thread.SetData(VModeSlot, (Object)cVmode);
                Thread.Sleep(delay * 1000);
                KillPlayer(name);
            }));

            delayed_kill.IsBackground = true;
            delayed_kill.Name = "delayed_kill";
            delayed_kill.Start();

            return !cVmode;
        }

        public Boolean KillPlayer(String name)
        {
            if (VMode)
            {
                ConsoleWarn("not killing ^b" + name + "^n, ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            if (!players.ContainsKey(name))
                return false;


            this.ServerCommand("admin.killPlayer", name);
            return true;
        }

        public Boolean KickPlayerWithMessage(String name, String message)
        {
            return KickPlayerWithMessage(name, message, true);
        }

        public Boolean KickPlayerWithMessage(String name, String message, Boolean tweet)
        {

            if (VMode)
            {
                ConsoleWarn("not kicking ^b" + name + "^n, ^bvirtual_mode^n is ^bon^n");
                return false;
            }


            if (isInWhitelist(name))
            {
                ConsoleWarn("not kicking ^b" + name + "^n, in white-list");
                return false;
            }

            this.ExecuteCommand("procon.protected.send", "admin.kickPlayer", name, message);
            RemovePlayer(name);

            if (getBooleanVarValue("tweet_my_server_kicks") && tweet)
                DefaultTweet("#Kick " + name + ",  @\"" + server_name + "\", for " + message + "");

            return true;
        }



        public Boolean EABanPlayerWithMessage(EABanType type, EABanDuration duration, String name, Int32 minutes, String message)
        {

            if (VMode)
            {
                ConsoleWarn("not ea-banning ^b" + name + "^n, ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            if (!players.ContainsKey(name))
            {
                ConsoleError("cannot find player ^b" + name + "^n, not ea-banning");
                return false;
            }

            if (isInWhitelist(name))
            {
                ConsoleWarn("not ea-banning ^b" + name + "^n, in white-list");
                return false;
            }

            PlayerInfo player = players[name];

            // get the type field and value
            String typeField = "guid";
            String typeValue = player.EAGuid;

            if (type.Equals(EABanType.EA_GUID))
            {
                typeField = "guid";
                typeValue = player.EAGuid;
            }
            else if (type.Equals(EABanType.IPAddress))
            {

                typeField = "ip";

                typeValue = player.pbInfo.Ip;

                // remove the port number
                typeValue = Regex.Replace(typeValue, ":(.+)$", "");

                typeValue = typeValue.Trim();
            }
            else if (type.Equals(EABanType.Name))
            {
                typeField = "name";
                typeValue = player.Name;
            }

            // get the time out value
            String timeout = "seconds";
            if (duration.Equals(EABanDuration.Permanent))
                timeout = "perm";
            else if (duration.Equals(EABanDuration.Round))
                timeout = "rounds";
            else if (duration.Equals(EABanDuration.Temporary))
                timeout = "seconds";



            String suffix = String.Empty;

            if (duration.Equals(EABanDuration.Temporary))
                suffix = "(" + EABanDuration.Temporary.ToString() + "/" + minutes.ToString() + ")";
            else if (duration.Equals(EABanDuration.Round))
                suffix = "(" + EABanDuration.Round.ToString() + ")";
            else if (duration.Equals(EABanDuration.Permanent))
                suffix = "(" + EABanDuration.Permanent.ToString() + ")";

            String ea_message = message + suffix;

            Int32 max_length = 80;
            if (ea_message.Length > max_length)
                ea_message = ea_message.Substring(0, max_length);

            if (duration.Equals(EABanDuration.Temporary))
                this.ExecuteCommand("procon.protected.send", "banList.add", typeField, typeValue, timeout, (minutes * 60).ToString(), ea_message);
            else if (duration.Equals(EABanDuration.Round))
                this.ExecuteCommand("procon.protected.send", "banList.add", typeField, typeValue, timeout, (1).ToString(), ea_message);
            else
                this.ExecuteCommand("procon.protected.send", "banList.add", typeField, typeValue, timeout, ea_message);

            this.ExecuteCommand("procon.protected.send", "banList.save");

            if (getBooleanVarValue("tweet_my_server_bans"))
                DefaultTweet("#EABan " + suffix + " " + name + " @\"" + server_name + "\", for " + message);

            KickPlayerWithMessage(name, message, false);

            return true;
        }



        public Boolean MovePlayer(String name, Int32 TeamId, Int32 SquadId, Boolean force)
        {
            if (VMode)
            {
                ConsoleWarn("not moving ^b" + name + "^n, ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            if (!players.ContainsKey(name))
            {
                ConsoleError("cannot find player ^b" + name + "^n, not moving");
                return false;
            }

            if (isInWhitelist(name))
            {
                ConsoleWarn("^b" + name + "^n is in white-list, not moving");
                return false;
            }

            this.ServerCommand("admin.movePlayer", name, TeamId.ToString(), SquadId.ToString(), force.ToString().ToLower());
            return true;
        }


        public Boolean PBCommand(String text)
        {

            if (VMode)
            {
                ConsoleWarn("not sending pb-command \"" + text + "\", ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            this.PunkBusterCommand(text);

            return true;
        }

        public Boolean SCommand(String text)
        {

            if (VMode)
            {
                ConsoleWarn("not sending server-command \"" + text + "\", ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            List<String> words = ParseCommand(new StringReader(text + "\n"));

            if (words == null || words.Count == 0)
                return false;

            this.ServerCommand(words.ToArray());

            return true;
        }

        /* simple command line parser */
        public List<String> ParseCommand(StringReader sin)
        {
            /* simple parser for command line */
            Boolean inside_string = false;
            Boolean previous_space = false;
            Boolean escape_char = false;

            String word = "";
            Char c = (Char)0;
            Int32 data = -1;

            List<String> words = new List<String>();

            while ((data = sin.Read()) != -1)
            {
                c = (Char)data;

                /* escaping quotes inside string */
                if (escape_char == true && c == (Char)'"' && inside_string == true)
                {
                    word += Char.ToString(c);
                    escape_char = false;
                    continue;
                }
                /* escaping the escape character anywhere */
                else if (escape_char == true && c == (Char)'\\')
                {
                    word += Char.ToString(c);
                    escape_char = false;
                    continue;
                }
                /* handle line continuation */
                else if (escape_char == true && c == (Char)'n')
                {
                    if (inside_string)
                        word += Char.ToString('\n');
                    escape_char = false;
                    continue;
                }
                else if (escape_char == true && c == (Char)'t')
                {
                    word += Char.ToString('\t');
                    escape_char = false;
                    continue;
                }
                else if (escape_char == true)
                {
                    /* finish readling the line */
                    sin.ReadLine();
                    ConsoleError("unknown escape sequence \\" + Char.ToString(c));
                    return new List<String>();
                }
                /* detect start of string */
                else if (c == (Char)'"' && inside_string == false)
                    inside_string = true;
                /* detect end of string */
                else if (c == (Char)'"' && inside_string == true)
                    inside_string = false;
                /* detect escape character */
                else if (c == (Char)'\\')
                    escape_char = true;
                /* detect unterminated stirng literal */
                else if (c == (Char)'\n' && inside_string == true)
                {
                    ConsoleError("unterminated String literal");
                    return new List<String>();
                }
                /* skip white-space */
                else if (inside_string == false && previous_space == true &&
                        (c == (Char)' ' || c == (Char)'\t'))
                    continue;
                /* detect end of word */
                else if (inside_string == false &&
                        (c == (Char)' ' || c == (Char)'\t' ||
                         c == (Char)'\n' || c == (Char)'\r'))
                {
                    previous_space = true;
                    word = word.Trim();
                    if (word.Length > 0)
                        words.Add(word);
                    word = "";

                    if (c == (Char)'\n')
                        return words;
                }
                else
                {
                    word += Char.ToString(c);
                    previous_space = false;
                    escape_char = false; /* fail-safe */
                }

            }

            return null;
        }


        public Boolean PBBanPlayerWithMessage(PBBanDuration duration, String name, Int32 minutes, String message)
        {

            if (VMode)
            {
                ConsoleWarn("not pb-banning ^b" + name + "^n, ^bvirtual_mode^n is ^bon^n");
                return false;
            }

            if (!players.ContainsKey(name))
            {
                ConsoleError("cannot find player ^b" + name + "^n, not pb-banning");
                return false;
            }

            if (isInWhitelist(name))
            {
                ConsoleWarn("^b" + name + "^n is in white-list, not pb-banning");
                return false;
            }

            String suffix = String.Empty;

            if (duration.Equals(PBBanDuration.Temporary))
                suffix = "(" + PBBanDuration.Temporary.ToString() + ":" + minutes + ")";
            else if (duration.Equals(PBBanDuration.Permanent))
                suffix = "(" + PBBanDuration.Permanent.ToString() + ")";

            String pb_message = message + suffix;


            if (duration.Equals(PBBanDuration.Permanent))
                this.ServerCommand("punkBuster.pb_sv_command", String.Join(" ", new String[] { "pb_sv_ban", name, pb_message, "|", "BC2!" }));
            else if (duration.Equals(PBBanDuration.Temporary))
                this.ServerCommand("punkBuster.pb_sv_command", String.Join(" ", new String[] { "pb_sv_kick", name, (minutes).ToString(), pb_message, "|", "BC2!" }));
            else
            {
                ConsoleError("unknown pb-ban duration, not pb-banning ^b" + name + "^n");
                return false;
            }

            this.ServerCommand("punkBuster.pb_sv_command", String.Join(" ", new String[] { "pb_sv_updbanfile" }));

            if (getBooleanVarValue("tweet_my_server_bans"))
                DefaultTweet("#PBBan " + suffix + " " + name + " @\"" + server_name + "\", for " + message);

            KickPlayerWithMessage(name, message, false);

            return true;
        }


        public static String list2string(List<String> list, String glue)
        {

            if (list == null || list.Count == 0)
                return "";
            else if (list.Count == 1)
                return list[0];

            String last = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);

            String str = "";
            foreach (String item in list)
                str += item + ", ";

            return str + glue + last;
        }


        public static String Encode(String str)
        {
            Byte[] encbuff = System.Text.Encoding.UTF8.GetBytes(str);
            return Convert.ToBase64String(encbuff);
        }
        public static String Decode(String str)
        {
            Byte[] decbuff = Convert.FromBase64String(str.Replace(" ", "+"));
            return System.Text.Encoding.UTF8.GetString(decbuff);
        }

        public Boolean isInList(String item, String list_name)
        {
            try
            {
                if (item == null || list_name == null)
                    return false;

                if (!getBooleanVarValue("use_custom_lists"))
                    return false;


                foreach (KeyValuePair<String, CustomList> pair in lists)
                    if (pair.Value != null && pair.Value.Name.Equals(list_name))
                        return pair.Value.Contains(item);
            }
            catch (Exception e)
            {
                DumpException(e);
            }

            return false;

        }

        public Boolean isInWhitelist(String player)
        {
            return isInPlayerWhitelist(player) || isInClanWhitelist(player);
        }

        public Boolean isInPlayerWhitelist(String player)
        {
            return isInWhiteList(player, "player_white_list");
        }

        public Boolean isInClanWhitelist(String player)
        {
            return isInWhiteList(player, "clan_white_list");
        }


        public Boolean isInWhiteList(String name, String list_name)
        {
            if (!getBooleanVarValue("use_white_list"))
                return false;

            if (!getPluginVars().Contains(list_name))
            {
                ConsoleWarn("unknown white list ^b" + list_name + "^n");
                return false;
            }

            List<String> whitelist = getStringListVarValue(list_name);
            if (whitelist.Count == 0)
                return false;


            String field = "";
            if (Regex.Match(list_name, @"clan").Success)
            {
                /* make sure player is in the list */
                if (!players.ContainsKey(name))
                {
                    ConsoleWarn("could not check if ^b" + name + "^n is in clan white list, he is not in interval players list");
                    return false;
                }
                field = players[name].Tag;
            }
            else if (Regex.Match(list_name, @"player").Success)
                field = name;
            else
            {
                ConsoleWarn("white list ^b" + list_name + "^n does not contain 'player' or 'clan' sub-String");
                return false;
            }

            if (Regex.Match(field, @"^\s*$").Success)
                return false;

            return whitelist.Contains(field);
        }

        public List<String> GetReservedSlotsList()
        {
            return reserved_slots_list;
        }

        public static String makeRelativePath(String file)
        {
            String exe_path = Directory.GetParent(Environment.ProcessPath).FullName;
            String dll_path = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName;

            String rel_path = dll_path.Replace(exe_path, "");
            rel_path = Path.Combine(rel_path.Trim(new Char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }), file);
            return rel_path;
        }

        public void dumpPairs(Dictionary<String, String> pairs, Int32 fields_line)
        {
            Int32 fcount = pairs.Count;
            Int32 fline = fields_line;

            List<String> lines = new List<String>();
            String line = "";
            List<String> keys = new List<String>(pairs.Keys);

            for (Int32 i = 0, j = 1; i < fcount; i++, j++)
            {
                String glue = "";
                if (line.Length == 0)
                    glue = "";

                line += String.Format("{0,30}", glue + keys[i] + "(" + pairs[keys[i]] + ")");
                if (j == fline)
                {
                    lines.Add(line);
                    j = 0;
                    line = "";
                }
            }

            if (line.Length > 0)
                lines.Add(line);

            foreach (String cline in lines)
                ConsoleWrite(cline);
        }

        public void dumpPairs(Dictionary<String, String> pairs, Int32 indent, String logName)
        {
            Int32 fcount = pairs.Count;
            List<String> keys = new List<String>(pairs.Keys);
            String line = "";
            Boolean first = true;

            for (Int32 i = 1; i <= indent; ++i)
            {
                line = line + " ";
            }

            foreach (String k in keys)
            {
                if (first)
                {
                    first = false;
                    line = line + k + ":" + pairs[k];
                }
                else
                {
                    line = line + ", " + k + ":" + pairs[k];
                }
            }

            if (logName == null)
            {
                ConsoleWrite(line);
            }
            else
            {
                Log(logName, line);
            }
        }

        public List<PropertyInfo> getProperties(Type type, String scope)
        {
            List<PropertyInfo> plist = new List<PropertyInfo>();

            //use refection to get the field names

            PropertyInfo[] props = type.GetProperties();
            for (Int32 i = 0; i < props.Length; i++)
            {
                Object[] attrs = props[i].GetCustomAttributes(true);

                if (attrs.Length > 0 && typeof(A).Equals(attrs[0].GetType()))
                {
                    A src = (A)attrs[0];
                    if (src.Scope.ToLower().Equals(scope.ToLower()))
                        plist.Add(props[i]);
                }
            }

            // sort the properties by name length, and if same length do it alphabetically
            plist.Sort(delegate (PropertyInfo left, PropertyInfo right)
            {
                Int32 cmp = left.Name.Length.CompareTo(right.Name.Length);
                if (cmp == 0)
                    return left.Name.CompareTo(right.Name);

                return cmp;
            });

            return plist;
        }

        public Dictionary<String, String> buildPairs(Object Object, List<PropertyInfo> plist)
        {
            Dictionary<String, String> pairs = new Dictionary<String, String>();

            for (Int32 i = 0; i < plist.Count; i++)
            {

                PropertyInfo prop = plist[i];
                String name = prop.Name;


                Double value = 0;

                if (!Double.TryParse(prop.GetValue(Object, null).ToString(), out value))
                {
                    ConsoleWarn("cannot cast " + Object.GetType().Name + "." + name + ", from " + prop.PropertyType.Name + " to " + value.GetType().Name);
                    value = Double.NaN;
                }

                pairs.Add(name, Math.Round(value, 2).ToString());
            }
            return pairs;
        }

        private List<String> splitMessageText(String text, Int32 max_length)
        {
            List<String> lines = new List<String>();
            while (text.Length > max_length)
            {
                String line = text.Substring(0, max_length);
                text = text.Substring(max_length);

                if (Regex.Match(line[max_length - 1].ToString(), @"[a-zA-Z0-9]$").Success)
                {
                    String putback = "";
                    for (Int32 i = max_length - 1; i >= 0 && !Regex.Match(line[i].ToString(), @"\s+").Success; i--)
                        putback = line[i] + putback;

                    if (putback.Length < max_length)
                    {
                        line = line.Substring(0, max_length - putback.Length);
                        text = putback + text;
                    }
                }

                lines.Add(line);
            }

            if (text.Length > 0)
                lines.Add(text);

            return lines;
        }

        public Double Aggregate(String property_name, Type type, Dictionary<String, Object> data)
        {
            Double total = 0;
            PropertyInfo property = type.GetProperty(property_name);

            if (property == null)
            {
                ConsoleError(type.Name + ".^b" + property_name + "^n does not exist");
                return 0;
            }

            foreach (KeyValuePair<String, Object> pair in data)
            {
                if (pair.Value == null)
                    continue;

                Double value = 0;
                // I know this is awefully slow, but better be safe for future changes
                if (!Double.TryParse(property.GetValue(pair.Value, null).ToString(), out value))
                {
                    ConsoleError(type.Name + "." + property.Name + ", cannot be cast to ^b" + typeof(Double).Name + "^n");
                    return 0;
                }
                total += value;
            }

            return total;
        }


        /* External plugin support */

        public Boolean IsOtherPluginEnabled(String className, String methodName)
        {
            List<MatchCommand> registered = this.GetRegisteredCommands();
            foreach (MatchCommand command in registered)
            {
                if (command.RegisteredClassname.Equals(className) && command.RegisteredMethodName.Equals(methodName))
                {
                    return true;
                }
            }
            return false;
        }

        public void CallOtherPlugin(String className, String methodName, Hashtable parms)
        {
            try
            {
                String json = JSON.JsonEncode(parms);
                String filtered = json.Replace('{', '(').Replace('}', ')');
                filtered = filtered.Substring(0, Math.Min(filtered.Length, 512));
                DebugWrite("Calling ^b" + className + "." + methodName + "^n(" + filtered + "...)", 5);
                this.ExecuteCommand("procon.protected.plugins.call", className, methodName, this.GetType().Name, json);
            }
            catch (Exception e)
            {
                DumpException(e);
            }
        }

        public DateTime GetLastPluginDataUpdate() // return timestamp for the last time InsaneLimits.UpdatePluginData() was called
        {
            return last_data_change;
        }

        public void UpdatePluginData(params String[] parms)
        {
            if (parms.Length != 4)
            {
                ConsoleWarn("UpdatePluginData called with incorrect parameter count: " + parms.Length);
                return;
            }
            /*
            parms[0]: Name of caller (plugin class)
            parms[1]: Name of the dictionary type: "Boolean", "Double", "Int32", "String" (not possible to pass Object type)
            parms[2]: Key
            parms[3]: Stringification of value
            */
            if (String.IsNullOrEmpty(parms[0]))
            {
                ConsoleWarn("UpdatePluginData parms[0]: caller name is invalid!");
                return;
            }
            if (String.IsNullOrEmpty(parms[1]))
            {
                ConsoleWarn("UpdatePluginData parms[1]: type is invalid!");
                return;
            }
            if (String.IsNullOrEmpty(parms[2]))
            {
                ConsoleWarn("UpdatePluginData parms[2]: key is invalid!");
                return;
            }
            try
            {
                String calledFrom = parms[0];
                Type type = typeof(String);
                switch (parms[1])
                {
                    case "Boolean": type = typeof(Boolean); break;
                    case "Double": type = typeof(Double); break;
                    case "Int32": type = typeof(Int32); break;
                    default: break;
                }
                String key = parms[2];
                Object value = parms[3];

                if (type == typeof(Boolean))
                {
                    Boolean v = false;
                    Boolean.TryParse(parms[3], out v);
                    value = (Boolean)v;
                }
                else if (type == typeof(Double))
                {
                    Double v = 0;
                    Double.TryParse(parms[3], out v);
                    value = (Double)v;
                }
                else if (type == typeof(Int32))
                {
                    Int32 v = 0;
                    Int32.TryParse(parms[3], out v);
                    value = (Int32)v;
                }

                DataDict.set(type, key, value);
                last_data_change = DateTime.Now;
                DebugWrite("Plugin ^b" + calledFrom + "^n, updated (" + parms[1] + ") plugin.Data[" + key + "]", 5);
            }
            catch (Exception) { }
        }

        /* Stats fetching support */

        public Boolean IsCacheEnabled(Boolean verbose)
        {
            List<MatchCommand> registered = this.GetRegisteredCommands();
            foreach (MatchCommand command in registered)
            {
                if (command.RegisteredClassname.Equals("CBattlelogCache") && command.RegisteredMethodName.Equals("PlayerLookup"))
                {
                    if (verbose) DebugWrite("^bBattlelog Cache^n plugin will be used for stats fetching!", 3);
                    return true;
                }
                else
                {
                    DebugWrite("Registered P: " + command.RegisteredClassname + ", M: " + command.RegisteredMethodName, 7);
                }
            }
            if (verbose) DebugWrite("^1^bBattlelog Cache^n plugin is disabled; installing/updating and enabling the plugin is recommended for Insane Limits!", 3);
            return false;
        }

        public String SendCacheRequest(String playerName, String requestType)
        {
            /* 
            Called in the fetch_thread_loop thread, but defined in the
            main class in order to have access to all the wait handles.
            */
            Hashtable request = new Hashtable();
            request["playerName"] = playerName;
            request["pluginName"] = "InsaneLimits";
            request["pluginMethod"] = "CacheResponse";
            request["requestType"] = requestType;

            // Set up response entry
            lock (cacheResponseTable)
            {
                cacheResponseTable[playerName] = null;
            }

            // Send request
            if (!plugin_enabled) return String.Empty;
            DateTime since = DateTime.Now;
            this.ExecuteCommand("procon.protected.plugins.call", "CBattlelogCache", "PlayerLookup", JSON.JsonEncode(request));

            // block for reply
            DebugWrite("^b" + requestType + "(" + playerName + ")^n, waiting for cache to respond", 5);
            Double maxWait = Convert.ToDouble(getIntegerVarValue("wait_timeout"));
            while (DateTime.Now.Subtract(since).TotalSeconds < maxWait)
            {
                if (!plugin_enabled) return String.Empty;
                // Give some time for the cache to respond
                reply_handle.WaitOne(500);
                reply_handle.Reset();
                if (!plugin_enabled) return String.Empty;
                lock (cacheResponseTable)
                {
                    if (cacheResponseTable.ContainsKey(playerName) && cacheResponseTable[playerName] != null) break;
                }
            }

            Boolean ok = false;

            lock (cacheResponseTable)
            {
                ok = (cacheResponseTable.ContainsKey(playerName) && cacheResponseTable[playerName] != null);
            }
            if (!ok)
            {
                DebugWrite(requestType + "(" + playerName + ") timed out, request exceeded " + maxWait.ToString("F1") + " seconds! Network congestion or another plugin lagging Procon?", 4);
                lock (cacheResponseTable)
                {
                    if (cacheResponseTable.ContainsKey(playerName))
                    {
                        cacheResponseTable.Remove(playerName);
                    }
                }
                return String.Empty;
            }

            String r = String.Empty;

            lock (cacheResponseTable)
            {
                if (cacheResponseTable.ContainsKey(playerName))
                {
                    r = cacheResponseTable[playerName];
                    cacheResponseTable.Remove(playerName);
                }
            }

            Hashtable header = (Hashtable)JSON.JsonDecode(r);

            if (header == null)
            {
                DebugWrite(requestType + "(" + playerName + "), failed, header is null", 4);
                return r;
            }

            Double fetchTime = -1;
            Double.TryParse((String)header["fetchTime"], out fetchTime);
            Double age = -1;
            Double.TryParse((String)header["age"], out age);

            if (fetchTime > 0)
            {
                DebugWrite(requestType + "(" + playerName + "), cache refreshed from Battlelog, took ^2" + fetchTime.ToString("F1") + " seconds", 5);
            }
            else if (age > 0)
            {
                TimeSpan a = TimeSpan.FromSeconds(age);
                DebugWrite(requestType + "(" + playerName + "), cached stats used, age is " + a.ToString().Substring(0, 8), 5);
            }
            DebugWrite("^2^bTIME^n took " + DateTime.Now.Subtract(since).TotalSeconds.ToString("F2") + " secs, cache lookup for " + playerName, 5);

            return r;
        }

        public void CacheResponse(params String[] response)
        {
            /*
            Called from the Battlelog Cache plugin Response thread
            */
            String val = null;
            DebugWrite("CacheResponse called with " + response.Length + " parameters", 5);
            if (getIntegerVarValue("debug_level") >= 5)
            {
                for (Int32 i = 0; i < response.Length; ++i)
                {
                    DebugWrite("#" + i + ") Length: " + response[i].Length, 5);
                    val = response[i];
                    if (val.Length > 100) val = val.Substring(0, 100) + " ... ";
                    if (val.Contains("{")) val = val.Replace('{', '<').Replace('}', '>'); // ConsoleWrite doesn't like messages with "{" in it
                    DebugWrite("#" + i + ") Value: " + val, 5);
                }
            }

            String key = response[0]; // Player's name
            val = response[1]; // JSON String

            Boolean ok = false;
            lock (cacheResponseTable)
            {
                ok = cacheResponseTable.ContainsKey(key);
            }

            if (!ok)
            {
                DebugWrite("^1WARNING: Unknown cache response for " + key + " (perhaps request timed out?)", 4);
                return;
            }

            lock (cacheResponseTable)
            {
                ok = (cacheResponseTable[key] == null);

            }
            if (!ok)
            {
                DebugWrite("^1WARNING: Cache response collision for " + key, 4);
                return;
            }

            lock (cacheResponseTable)
            {
                cacheResponseTable[key] = val;
            }
            DebugWrite("CacheResponse reply, signal SendCacheRequest to unblock", 7);
            reply_handle.Set();
        }

        /* R38/Procon 1.4.0.7 */


        public override void OnPlayerIdleDuration(String soldierName, Int32 idleTime)
        {
            DebugWrite("Got ^bOnPlayerIdleDuration^n: " + soldierName + ", " + idleTime, 8);
            if (!plugin_activated) return;

            try
            {
                if (String.IsNullOrEmpty(soldierName)) return;

                PlayerInfo pinfo = null;

                lock (players_mutex)
                {
                    if (players.ContainsKey(soldierName))
                    {
                        players.TryGetValue(soldierName, out pinfo);
                    }
                }

                if (pinfo == null) return;

                pinfo._idleTime = Math.Max(0, idleTime);
            }
            catch (Exception e)
            {
                DebugWrite(e.Message, 5);
            }
        }

        public override void OnPlayerPingedByAdmin(String soldierName, Int32 ping)
        {
            DebugWrite("Got ^bOnPlayerPingedByAdmin^n: " + soldierName + ", " + ping, 8);
            if (!plugin_activated) return;

            try
            {
                if (String.IsNullOrEmpty(soldierName)) return;

                PlayerInfo pinfo = null;

                lock (players_mutex)
                {
                    if (players.ContainsKey(soldierName))
                    {
                        players.TryGetValue(soldierName, out pinfo);
                    }
                }

                if (pinfo == null) return;

                Int32 lastPing = Math.Max(0, Math.Min(ping, 1000));
                pinfo.Ping = lastPing;
                if (pinfo.MaxPing < lastPing) pinfo.MaxPing = lastPing;
                if (pinfo.MinPing > lastPing) pinfo.MinPing = lastPing;

                // Update median and average
                const Int32 PQLEN = 5;
                const Int32 PQMED = 2; // median index for PQLEN
                Boolean changeInPing = true;
                if (pinfo._pingQ.Count == PQLEN)
                {
                    // If last ping duplicates the median, skip it
                    changeInPing = (pinfo.MedianPing != lastPing);
                }

                if (changeInPing)
                {
                    pinfo._pingQ.Enqueue(lastPing);
                    while (pinfo._pingQ.Count > PQLEN) pinfo._pingQ.Dequeue();
                    List<Int32> p = new List<Int32>(pinfo._pingQ);
                    //  Average just needs more than 1, doesn't matter if it is sorted
                    if (p.Count > 1)
                    {
                        Int32 sum = 0;
                        foreach (Int32 i in p)
                        {
                            sum = sum + i;
                        }
                        pinfo.AveragePing = sum / p.Count;
                    }
                    else
                    {
                        pinfo.AveragePing = lastPing;
                    }
                    // Median must be PQLEN exactly
                    if (p.Count == PQLEN)
                    {
                        p.Sort();
                        pinfo.MedianPing = p[PQMED];
                    }
                    else
                    {
                        // Otherwise it is the same as average
                        pinfo.MedianPing = pinfo.AveragePing;
                    }
                }
            }
            catch (Exception e)
            {
                DebugWrite(e.Message, 5);
            }
        }

        public override void OnSquadLeader(Int32 teamId, Int32 squadId, String soldierName)
        {
            DebugWrite("Got ^bOnSquadLeader^n: " + soldierName + ", " + teamId + ", " + squadId, 8);

            if (teamId == 0 || squadId == 0)
                return;

            String key = teamId.ToString() + "/" + squadId;
            squadLeaders[key] = soldierName;

            resetUpdateTimer(WhichTimer.Squad);
        }

        public override void OnSquadIsPrivate(Int32 teamId, Int32 squadId, Boolean isPrivate)
        {
            DebugWrite("Got ^bOnSquadIsPrivate^n: " + teamId + ", " + squadId + ", " + isPrivate, 8);

            if (teamId == 0 || squadId == 0) return;

            String key = teamId.ToString() + "/" + squadId;
            if (isPrivate && !lockedSquads.Contains(key)) lockedSquads.Add(key);
            else if (!isPrivate && lockedSquads.Contains(key)) lockedSquads.Remove(key);

            resetUpdateTimer(WhichTimer.Squad);
        }

        public override void OnCtfRoundTimeModifier(Int32 limit)
        {
            DebugWrite("Got ^bOnCtfRoundTimeModifier^n: " + limit, 8);
            if (!plugin_activated) return;

            this.ctfRoundTimeModifier = limit;
        }

        public override void OnGunMasterWeaponsPreset(Int32 preset)
        {
            DebugWrite("Got ^bOnGunMasterWeaponsPreset^n: " + preset, 8);

            this.varGunMasterWeaponsPreset = preset;

            resetUpdateTimer(WhichTimer.Vars);
        }

        public override void OnVehicleSpawnAllowed(Boolean isEnabled)
        {
            DebugWrite("Got ^bOnVehicleSpawnAllowed^n: " + isEnabled, 8);

            this.varVehicleSpawnAllowed = isEnabled;

            resetUpdateTimer(WhichTimer.Vars);
        }

        public override void OnVehicleSpawnDelay(Int32 limit)
        {
            DebugWrite("Got ^bOnVehicleSpawnDelay^n: " + limit, 8);

            this.varVehicleSpawnDelay = limit;

            resetUpdateTimer(WhichTimer.Vars);
        }

        public override void OnBulletDamage(Int32 limit)
        {
            DebugWrite("Got ^bOnBulletDamage^n: " + limit, 8);

            this.varBulletDamage = limit;

            resetUpdateTimer(WhichTimer.Vars);
        }

        public override void OnSoldierHealth(Int32 limit)
        {
            DebugWrite("Got ^bOnSoldierHealth^n: " + limit, 8);

            this.varSoldierHealth = limit;

            resetUpdateTimer(WhichTimer.Vars);
        }

        public override void OnGameModeCounter(Int32 limit)
        {
            DebugWrite("Got ^bOnGameModeCounter^n: " + limit, 8);
            if (!plugin_activated) return;

            this.gameModeCounter = limit;

            resetUpdateTimer(WhichTimer.Vars);
        }

        public override void OnFriendlyFire(Boolean isEnabled)
        {
            DebugWrite("Got ^bOnFriendlyFire^n: " + isEnabled, 8);

            this.varFriendlyFire = isEnabled;

            resetUpdateTimer(WhichTimer.Vars);
        }

        public override void OnIdleTimeout(Int32 limit)
        {
            DebugWrite("Got ^bOnIdleTimeout^n: " + limit, 8);

            this.varIdleTimeout = limit;

            resetUpdateTimer(WhichTimer.Vars);
        }

        public void OnPluginLoadingEnv(List<String> lstPluginEnv)
        {
            foreach (String env in lstPluginEnv)
            {
                DebugWrite("Got ^bOnPluginLoadingEnv: " + env, 8);
            }
            game_version = lstPluginEnv[1];
            ConsoleWrite("^2Game Version = " + lstPluginEnv[1]);
        }

        // BF4

        public override void OnCommander(Boolean isEnabled)
        {
            DebugWrite("Got ^bOnCommander^n: " + isEnabled, 5);

            this.varCommander = isEnabled;

            resetUpdateTimer(WhichTimer.Vars);
        }

        public override void OnMaxSpectators(Int32 limit)
        {
            DebugWrite("Got ^bOnMaxSpectators^n: " + limit, 8);

            this.varMaxSpectators = limit;

            resetUpdateTimer(WhichTimer.Vars);
        }

        public override void OnServerType(String value)
        {
            DebugWrite("Got ^bOnServerType^n: " + value, 8);

            this.varServerType = value;

            resetUpdateTimer(WhichTimer.Vars);
        }

        public override void OnTeamFactionOverride(Int32 teamId, Int32 faction)
        {
            DebugWrite("Got ^bOnTeamFactionOverride^n: " + teamId + " " + faction, 8);

            if (this.serverInfo._Faction != null && teamId >= 0 && teamId < this.serverInfo._Faction.Length)
            {
                this.serverInfo._Faction[teamId] = faction;
            }
        }

    }
}
