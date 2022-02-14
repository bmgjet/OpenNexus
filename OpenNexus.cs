//None of this code is to be used in any paid projects/plugins
using Facepunch;
using Facepunch.Utility;
using Network;
using Oxide.Core;
using ProtoBuf;
using Rust.Modular;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Oxide.Core.Database;
using Rust;
using Oxide.Core.Plugins;
using Newtonsoft.Json;
using System.Linq;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins
{
    [Info("OpenNexus", "bmgjet", "1.0.0")]
    [Description("Nexus system created by bmgjet")]
    public class OpenNexus : RustPlugin
    {
        private string thisserverip = "";        //Over-ride auto detected ip
        private string thisserverport = "";      //Over-ride auto detected port
        //Vehicle to apply edge protection too (no point doing cars ect since they will sink before they get there)
        private string[] BaseVehicle = { "rowboat", "rhib", "scraptransporthelicopter", "minicopter.entity", "submarinesolo.entity", "submarineduo.entity" };

        //Permissions
        private readonly string permbypass = "OpenNexus.bypass"; //bypass the single server at a time limit
        private readonly string permadmin = "OpenNexus.admin";   //Allows to use admin commands

        //Memory
        public List<IslandData> FoundIslands = new List<IslandData>();
        private Dictionary<ulong, int> SeatPlayers = new Dictionary<ulong, int>();
        private List<BaseNetworkable> unloadable = new List<BaseNetworkable>();
        private List<ulong> ProcessingPlayers = new List<ulong>();
        private List<ulong> MovePlayers = new List<ulong>();
        private List<BasePlayer> JoinedViaNexus = new List<BasePlayer>();
        private List<ModuleData> CarModules = new List<ModuleData>();
        public static OpenNexus plugin;
        private Climate climate;
        private uint RanMapSize;

        //Plugin Hooks
        [PluginReference]
        private Plugin Backpacks, Economics, ZLevelsRemastered, ServerRewards;

        #region Configuration
        private static Configuration config = new Configuration();
        private class Configuration
        {
            [JsonProperty("Server Settings:")]
            public ServerSettings _ServerSettings = new ServerSettings();
            internal class ServerSettings
            {
                [JsonProperty("How long between mysql requests (dont go below 0.5)")]
                public float ServerDelay;
                [JsonProperty("Address of MySQL server")]
                public String MySQLHost;
                [JsonProperty("Port of MySQL server")]
                public int MySQLPort;
                [JsonProperty("Database name")]
                public string MySQLDB;
                [JsonProperty("MySQL login username")]
                public string MySQLUsername;
                [JsonProperty("MySQL login password")]
                public string MySQLPassword;
                [JsonProperty("Extend distance ferry travels before transfere (goes 100f from dock by default)")]
                public int ExtendFerryDistance;
                [JsonProperty("Ferry goes to nearest NexusIsland to teleport")]
                public bool AutoDistance;
                [JsonProperty("Sync weather between all servers")]
                public bool SyncTimeWeather;
                [JsonProperty("Resync the weather every (seconds dont set this too low)")]
                public int SyncTimeWeaterEvery;
                [JsonProperty("Protect edge of map from players driving off")]
                public bool EdgeTeleporter;
                [JsonProperty("Compress packets to save data (recommended since it 1/4 there size)")]
                public bool UseCompression;
                [JsonProperty("Print debug info to console")]
                public bool ShowDebugMsg;
            }
            [JsonProperty("Ferry Settings:")]
            public FerrySettings _FerrySettings = new FerrySettings();
            internal class FerrySettings
            {
                [JsonProperty("Speed that the ferry moves in water")]
                public float MoveSpeed;
                [JsonProperty("Speed that the ferry can rotate in water")]
                public float TurnSpeed;
                [JsonProperty("How long ferry waits at dock (seconds)")]
                public float WaitTime;
                [JsonProperty("How long after eject before ferry leaves dock (seconds)")]
                public float EjectDelay;
                [JsonProperty("How long ferry waits out at sea for transferes (seconds)")]
                public int TransfereTime;
                [JsonProperty("How long ferry waits out at sea for sync packet before giving up on starting transfere (seconds)")]
                public int TransfereSyncTime;
                [JsonProperty("Extra time after things spawn on ferry to wait before returning to dock (seconds)")]
                public int ProgressDelay;
                [JsonProperty("Extra time to wait between moving entitys and players")]
                public int RedirectDelay;
            }

            [JsonProperty("Status Settings:")]
            public StatusSettings _StatusSettings = new StatusSettings();
            internal class StatusSettings
            {
                [JsonProperty("Show status messages to players")]
                public bool StatusMsg;
                [JsonProperty("Font size")]
                public int FontSize;
                [JsonProperty("Font coluor")]
                public string FontColour;
                [JsonProperty("Fade in")]
                public int FontFadeIn;
                [JsonProperty("AnchorMin")]
                public string AnchorMin;
                [JsonProperty("AnchorMax")]
                public string AnchorMax;
                [JsonProperty("FailedMSG")]
                public string FailedMSG;
                [JsonProperty("WaitingMSG")]
                public string WaitingMSG;
                [JsonProperty("CastoffMSG")]
                public string CastoffMSG;
                [JsonProperty("ArriveMSG")]
                public string ArriveMSG;
            }
            [JsonProperty("Plugin Settings:")]
            public PluginSettings _PluginSettings = new PluginSettings();
            internal class PluginSettings
            {
                [JsonProperty("BackPacks items (Enabled)")]
                public bool BackPacks;
                [JsonProperty("Economics (Enabled)")]
                public bool Economics;
                [JsonProperty("ZLevelsRemastered (Enabled)")]
                public bool ZLevelsRemastered;
                [JsonProperty("ServerRewards (Enabled)")]
                public bool ServerRewards;
                [JsonProperty("BluePrints (Enabled)")]
                public bool BluePrints;
                [JsonProperty("Modifiers (Enabled)")]
                public bool Modifiers;
                [JsonProperty("Parented (Enabled)")]
                public bool Parented;
            }

            public static Configuration GetNewConfiguration()
            {
                return new Configuration
                {
                    _ServerSettings = new ServerSettings
                    {
                        ServerDelay = 1f,
                        MySQLHost = "localhost",
                        MySQLPort = 3306,
                        MySQLDB = "opennexus",
                        MySQLUsername = "OpenNexus",
                        MySQLPassword = "1234",
                        ExtendFerryDistance = 0,
                        AutoDistance = true,
                        SyncTimeWeather = true,
                        SyncTimeWeaterEvery = 360,
                        EdgeTeleporter = true,
                        UseCompression = true,
                        ShowDebugMsg = false
                    },
                    _FerrySettings = new FerrySettings
                    {
                        MoveSpeed = 12f,
                        TurnSpeed = 0.6f,
                        WaitTime = 60,
                        EjectDelay = 60,
                        TransfereTime = 60,
                        TransfereSyncTime = 60,
                        ProgressDelay = 60,
                        RedirectDelay = 5,
                    },
                    _StatusSettings = new StatusSettings
                    {
                        StatusMsg = true,
                        FontSize = 32,
                        FontColour = "0.1 0.4 0.1 0.7",
                        FontFadeIn = 3,
                        AnchorMin = "0.100 0.800",
                        AnchorMax = "0.900 0.900",
                        FailedMSG = "Other Servers Failed To Reach Transfere Point In Time",
                        WaitingMSG = "Waiting For Other Server",
                        CastoffMSG = "Ferry Casting Off In <$T> Seconds",
                        ArriveMSG = "You Have Arrived At The Next Server"
                    },
                    _PluginSettings = new PluginSettings
                    {
                        BackPacks = true,
                        Economics = true,
                        ZLevelsRemastered = true,
                        ServerRewards = true,
                        BluePrints = true,
                        Modifiers = true,
                        Parented = true
                    }
                };
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) { LoadDefaultConfig(); }
            }
            catch
            {
                PrintWarning($"Configuration read error 'oxide/config/{Name}', creating a new configuration !!");
                LoadDefaultConfig();
            }
            NextTick(SaveConfig);
        }

        protected override void LoadDefaultConfig() => config = Configuration.GetNewConfiguration();
        protected override void SaveConfig() => Config.WriteObject(config);
        #endregion

        #region Commands
        //Chat command to paste where admin is a transfered packet
        //Command /OpenNexus.Paste $PacketID
        [ChatCommand("OpenNexus.Paste")]
        private void cmdPaste(BasePlayer player, string command, string[] args)
        {
            if (!HasPermission(player, permadmin))
            {
                player.ChatMessage("You dont have OpenNexus.admin Perm!");
                return;
            }
            if (args.Length != 1)
            {
                player.ChatMessage("You must provide packet id!");
                return;
            }
            int packetid = int.Parse(args[0]);
            player.ChatMessage("Pasting Packet " + args[0]);
            //Do Read Of Packet
            MySQLRead("", null, packetid, player);
        }

        //Clears all the MySQL tables and sets back to default.
        [ConsoleCommand("OpenNexus.resettables")]
        private void cmdReset(ConsoleSystem.Arg arg)
        {
            //Resets database tables
            if (!arg.IsAdmin) { return; }

            string sqlQuery = "DROP TABLE IF EXISTS  players, packets, sync;";
            Sql deleteCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery);
            sqlLibrary.ExecuteNonQuery(deleteCommand, sqlConnection);
            if (config._ServerSettings.ShowDebugMsg) Puts("cmdReset Dropped all tables");
            timer.Once(config._ServerSettings.ServerDelay, () =>
            {
                CreatesTables();
                //Reset players data
                timer.Once(config._ServerSettings.ServerDelay, () =>{foreach (BasePlayer bp in BasePlayer.activePlayerList) { if (bp.IsConnected && !bp.IsNpc) { NextTick(() => { UpdatePlayers(thisserverip + ":" + thisserverport, thisserverip + ":" + thisserverport, "Playing", bp.UserIDString); }); } }});
            });
        }

        //Reloads the config with out having to restart, Not all changes will take effect
        [ChatCommand("OpenNexus.reloadconfig")]
        private void cmdreloadconfig(BasePlayer player, string command, string[] args)
        {
            if (!HasPermission(player, permadmin))
            {
                player.ChatMessage("You dont have OpenNexus.admin Perm!");
                return;
            }
            Configuration oldconfig = config;
            config = Config.ReadObject<Configuration>();
            if (oldconfig != config)
            {
                player.ChatMessage("Loaded changes, Not all settings will take effect with out a restart of plugin.");
                player.ChatMessage("You can restart plugin with /oxide.reload OpenNexus");
            }
        }

        //Resets config to defaults
        [ChatCommand("OpenNexus.resetconfig")]
        private void cmdresetconfig(BasePlayer player, string command, string[] args)
        {
            if (!HasPermission(player, permadmin))
            {
                player.ChatMessage("You dont have OpenNexus.admin Perm!");
                return;
            }
            LoadDefaultConfig();
            NextTick(SaveConfig);
            player.ChatMessage("Reset config to defaut. Not all settings will take effect with out a restart of plugin.");
            player.ChatMessage("You can restart plugin with /oxide.reload OpenNexus");
        }

        //toggle debug information
        [ChatCommand("OpenNexus.debug")]
        private void cmddebug(BasePlayer player, string command, string[] args)
        {
            if (!HasPermission(player, permadmin)){player.ChatMessage("You dont have OpenNexus.admin Perm!");return;}
            if (config._ServerSettings.ShowDebugMsg)
            {
                config._ServerSettings.ShowDebugMsg = false;
                player.ChatMessage("Debug messages disabled.");
            }
            else
            {
                config._ServerSettings.ShowDebugMsg = true;
                player.ChatMessage("Debug messages enabled.");
            }
            NextTick(SaveConfig);
        }

        [ConsoleCommand("OpenNexus.compression")]
        private void cmdComp(ConsoleSystem.Arg arg)
        {
            //Resets database tables
            if (!arg.IsAdmin) { return; }
            if (config._ServerSettings.ShowDebugMsg)
            {
                config._ServerSettings.UseCompression = false;
                Puts("Compression Disabled.");
            }
            else
            {
                config._ServerSettings.UseCompression = true;
                Puts("Compression Enabled.");
            }
            NextTick(SaveConfig);
        }
        #endregion

        #region MySQL
        //MySQL
        Core.MySql.Libraries.MySql sqlLibrary = Interface.Oxide.GetLibrary<Core.MySql.Libraries.MySql>();
        Core.Database.Connection sqlConnection;

        //Read data from mysql database
        private void MySQLRead(string Target, OpenNexusFerry OpenFerry, int findid = 0, BasePlayer player = null)
        {
            string sqlQuery;
            Sql selectCommand;
            //If passed as admin command to read given packet
            if (player != null)
            {
                sqlQuery = "SELECT `id`, `spawned`,`data` FROM packets WHERE `id` = @0;";
                selectCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, findid);
            }
            else
            {
                //Read last transfered packet waiting for us
                sqlQuery = "SELECT `id`, `spawned`,`data` FROM packets WHERE `target`= @0 AND `sender` = @1 ORDER BY id DESC LIMIT 10;";
                selectCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, Target, OpenFerry.ServerIP + ":" + OpenFerry.ServerPort);
            }
            sqlLibrary.Query(selectCommand, sqlConnection, list =>
            {
                if (list == null) { return; }
                foreach (Dictionary<string, object> entry in list)
                {
                    //Packet has already been spawned on server before so dont re-transfere
                    if (entry["spawned"].ToString() == "1" && findid == 0) { continue; } 
                    //Process Packet
                    int id;
                    bool success = int.TryParse(entry["id"].ToString(), out id);
                    if (!success) { continue; }
                    string data = "";
                    if (config._ServerSettings.UseCompression) { data = Encoding.UTF8.GetString(Compression.Uncompress(Convert.FromBase64String(entry["data"].ToString()))); } //compressed
                    else { data = entry["data"].ToString(); }
                    //Admin command
                    if (player != null)
                    {
                        ReadPacket(data, player, id);
                        return;
                    }
                    //Process mysql packet
                    ReadPacket(data, OpenFerry, id);
                    //Kill stray bots that might of followed players
                    List<BasePlayer> Bots = new List<BasePlayer>();
                    List<BasePlayer> KillBots = new List<BasePlayer>();
                    Vis.Entities<BasePlayer>(OpenFerry.transform.position + (OpenFerry.transform.rotation * Vector3.forward * 6), 14f, KillBots);
                    foreach (BasePlayer bp in KillBots) { if (!Bots.Contains(bp) && !bp.IsNpc) { Bots.Add(bp); } }
                    Vis.Entities<BasePlayer>(OpenFerry.transform.position + (OpenFerry.transform.rotation * Vector3.forward * -12), 14f, KillBots);
                    foreach (BasePlayer bp in KillBots) { if (!Bots.Contains(bp) && !bp.IsNpc) { Bots.Add(bp); } }
                    foreach (BasePlayer bot in Bots) { if (!IsSteamId(bot.UserIDString)) { bot.Kill(); } }
                    if (config._ServerSettings.ShowDebugMsg) plugin.Puts("Read " + String.Format("{0:0.##}", (double)(entry["data"].ToString().Length / 1024f)) + " Kb");
                    return;
                }
                    return;
            });
        }

        //Write data to mysql database
        private void MySQLWrite(string target = "", string sender = "", string data = "", int id = 0, int spawned = 0)
        {
            if (id != 0)
            {
                //If ID is given then update this as being spawned
                string sqlQuery = "UPDATE packets SET `spawned` = @1 WHERE `id` = @0;";
                Sql updateCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, id, spawned);
                sqlLibrary.Update(updateCommand, sqlConnection, rowsAffected =>
                {
                    if (rowsAffected > 0) { if (config._ServerSettings.ShowDebugMsg) { Puts("MySQLWrite Record Updated"); } return; }
                    else { if (config._ServerSettings.ShowDebugMsg) { Puts("MySQLWrite Record Update Failed!"); } return; }
                });
                return;
            }
            //No ID given so create new
            if (target != "" && sender != "" && data != "")
            {
                string sqlQuery = "INSERT INTO packets (`spawned`, `timestamp`, `target`,`sender`,`data`) VALUES (@0, @1, @2, @3, @4);";
                Sql insertCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, spawned, DateTime.Now.ToString("t"), target, sender, data);
                sqlLibrary.Insert(insertCommand, sqlConnection, rowsAffected =>
                {
                    if (rowsAffected > 0)
                    {
                        if (config._ServerSettings.ShowDebugMsg) { Puts("MySQLWrite New record inserted with ID: {0}", sqlConnection.LastInsertRowId); }
                        return;
                    }
                    else
                    {
                        if (config._ServerSettings.ShowDebugMsg) { Puts("MySQLWrite Failed to insert!"); }
                        return;
                    }
                });
            }
        }

        //Maintains table of each ferrys status
        private void UpdateSync(string fromaddress, string target, string state)
        {
            //try Update
            string sqlQuery = "UPDATE sync SET `state` = @0, `climate` = @3 WHERE `sender` = @1 AND `target` = @2;";
            Sql updateCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, state, fromaddress, target, plugin.getclimate()); ;
            sqlLibrary.Update(updateCommand, sqlConnection, rowsAffected =>
            {
                if (rowsAffected > 0) { if (config._ServerSettings.ShowDebugMsg) { Puts("UpdateSync Record Updated"); } return; }
                //Update failed so do insert
                sqlQuery = "INSERT INTO sync (`state`, `sender`, `target`, `climate`) VALUES (@0, @1, @2, @3);";
                Sql insertCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, state, fromaddress, target, plugin.getclimate());
                sqlLibrary.Insert(insertCommand, sqlConnection, rowsAffectedwrite =>
                {
                    if (rowsAffectedwrite > 0) { if (config._ServerSettings.ShowDebugMsg) { Puts("UpdateSync New Record inserted with ID: {0}", sqlConnection.LastInsertRowId); } }
                });
            });
        }

        //Checks the state of players
        private void ReadPlayers(string Target, ulong steamid, Network.Connection connection)
        {
            string sqlQuery = "SELECT `state`, `target`, `sender` FROM players WHERE `steamid` = @0;";
            Sql selectCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, steamid.ToString());
            sqlLibrary.Query(selectCommand, sqlConnection, list =>
            {
                if (list == null)
                {
                    //creates new player data
                    if (config._ServerSettings.ShowDebugMsg) { Puts("ReadPlayers No Player Data For  " + steamid.ToString() + " Creating It"); }
                    UpdatePlayers(Target, Target, "Playing", steamid.ToString());
                    return;
                }
                foreach (Dictionary<string, object> entry in list)
                {
                    //Redirect player to last server they were in.
                    if (entry["target"].ToString() != thisserverip + ":" + thisserverport && entry["state"].ToString() != "Moving" && entry["state"].ToString() != "Ready" && connection != null)
                    {
                        if (BasePlayer.FindByID(connection.ownerid).IPlayer.HasPermission(permbypass)) { return; }
                        string[] server = entry["target"].ToString().Split(':');
                        if (config._ServerSettings.ShowDebugMsg) Puts("ReadPlayers Redirecting  " + steamid);
                        ConsoleNetwork.SendClientCommand(connection, "nexus.redirect", new object[] { server[0], server[1] });
                        return;
                    }
                    //Waits for player moving
                    if (entry["state"].ToString() == "Moving")
                    {
                        if (config._ServerSettings.ShowDebugMsg) { Puts("ReadPlayers Waiting for server to set player as ready " + steamid); }
                        return;
                    }
                    //Sets flag to move player
                    if (entry["state"].ToString() == "Ready")
                    {
                        if (config._ServerSettings.ShowDebugMsg) { Puts("ReadPlayers Player Ready to move " + steamid); }
                        MovePlayers.Add(ulong.Parse(steamid.ToString()));
                        //Sets flag back to playing
                        UpdatePlayers(Target, Target, "Playing", steamid.ToString());
                        return;
                    }
                    return;
                }
                //Creates new player data
                if (config._ServerSettings.ShowDebugMsg) { Puts("No Player Data For  " + steamid + " Creating It"); }
                UpdatePlayers(Target, Target, "Playing", steamid.ToString());
            });
        }

        //Updates players table
        private void UpdatePlayers(string fromaddress, string target, string state, string steamid)
        {
            //trys to update
            string sqlQuery = "UPDATE players SET `state` = @0, `target` = @1, `sender` = @2 WHERE `steamid` = @3;";
            Sql updateCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, state, target, fromaddress, steamid);
            sqlLibrary.Update(updateCommand, sqlConnection, rowsAffected =>
            {
                if (rowsAffected > 0) { if (config._ServerSettings.ShowDebugMsg) { Puts("UpdatePlayers Record Updated"); } return; }
                //Failed to update so create new
                sqlQuery = "INSERT INTO players (`state`, `target`, `sender`,`steamid`) VALUES (@0, @1, @2, @3);";
                Sql insertCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, state, target, fromaddress, steamid);
                sqlLibrary.Insert(insertCommand, sqlConnection, rowsAffectedInsert => { if (rowsAffectedInsert > 0) { if (config._ServerSettings.ShowDebugMsg) { Puts("UpdatePlayers New Record inserted with ID: {0}", sqlConnection.LastInsertRowId); } } });
            });
        }

        //Connect to mysql database
        private void ConnectToMysql(string host, int port, string database, string username, string password)
        {
            sqlConnection = sqlLibrary.OpenDb(host, port, database, username, password, this);
            // Failed message
            if (sqlConnection == null || sqlConnection.Con == null) { Puts("Couldn't open the MySQL Database: {0} ", sqlConnection.Con.State.ToString()); }
            else { Puts("MySQL server connected: " + host); CreatesTables(); }
        }

        private void CreatesTables()
        {
            //Setup tables if they dont exsist
            sqlLibrary.Insert(Core.Database.Sql.Builder.Append("CREATE TABLE IF NOT EXISTS `packets` (`id` int(11) unsigned NOT NULL AUTO_INCREMENT, `spawned` int(1) NOT NULL,`timestamp` varchar(64) NOT NULL,`target` varchar(21),`sender` varchar(21),`data` mediumtext, PRIMARY KEY (`id`)) DEFAULT CHARSET=utf8;"), sqlConnection);
            sqlLibrary.Insert(Core.Database.Sql.Builder.Append("CREATE TABLE IF NOT EXISTS `sync` (`id` int(11) unsigned NOT NULL AUTO_INCREMENT,`sender` varchar(21),`target` varchar(21),`state` varchar(21),`climate` text, PRIMARY KEY (`id`)) DEFAULT CHARSET=utf8;"), sqlConnection);
            sqlLibrary.Insert(Core.Database.Sql.Builder.Append("CREATE TABLE IF NOT EXISTS `players` (`id` int(11) unsigned NOT NULL AUTO_INCREMENT,`sender` varchar(21),`target` varchar(21),`state` varchar(21),`steamid` varchar(21), PRIMARY KEY (`id`)) DEFAULT CHARSET=utf8;"), sqlConnection);
        }
        #endregion

        #region Oxidehooks
        private void Init()
        {
            //Set up permissions
            permission.RegisterPermission(permadmin, this);
            permission.RegisterPermission(permbypass, this);
        }

        private void OnServerInitialized(bool initial)
        {
            plugin = this;
            //Connect to database
            ConnectToMysql(config._ServerSettings.MySQLHost, config._ServerSettings.MySQLPort, config._ServerSettings.MySQLDB, config._ServerSettings.MySQLUsername, config._ServerSettings.MySQLPassword);
            //Get Weather data
            climate = SingletonComponent<Climate>.Instance;
            //Get world size for teleport zoning
            if (config._ServerSettings.EdgeTeleporter){RanMapSize = (uint)(World.Size / 1.02);if (RanMapSize >= 4000) { RanMapSize = 3900; }}
            //Sync weather repeat timer
            if (config._ServerSettings.SyncTimeWeather) { timer.Every(config._ServerSettings.SyncTimeWeaterEvery, () => setclimate()); }
            //Determing if need wait for first startup
            if (initial) { Fstartup(); }else { Startup(); }
        }

        private void OnEntitySpawned(BaseEntity baseEntity)
        {
            //Add edge teleport if server isnt loading
            if (!Rust.Application.isLoading && !Rust.Application.isLoadingSave)
            {
                if (config._ServerSettings.EdgeTeleporter)
                {
                    BaseVehicle vehicle = baseEntity as BaseVehicle;
                    if (vehicle != null) if (vehicle.GetComponent<EdgeTeleport>() == null) { vehicle.gameObject.AddComponent<EdgeTeleport>(); return; }
                }
            }
        }

        private void OnWorldPrefabSpawned(GameObject gameObject, string str)
        {
            //Remove Uncoded NexusFerry / NexusIsland
            BaseEntity component = gameObject.GetComponent<BaseEntity>();
            if (component != null) { if ((component.prefabID == 2508295857 || component.prefabID == 2795004596) && component.OwnerID == 0) { component.Kill(); } }
        }

        private void OnPlayerSetInfo(Network.Connection connection, string name, string value)
        {
            //Limits player to 1 server at a time.
            if (ProcessingPlayers.Contains(connection.ownerid)) return;
            ProcessingPlayers.Add(connection.ownerid);
            timer.Once(10f, () => ProcessingPlayers.Remove(connection.ownerid));
            if (config._ServerSettings.ShowDebugMsg) Puts("Checking if " + connection.ownerid.ToString() + " is already on any OpenNexus servers");
            ReadPlayers(thisserverip + ":" + thisserverport, connection.ownerid, connection);
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            //Mount players that were mounted.
            if (SeatPlayers.ContainsKey(player.userID))
            {
                MountPlayer(player, SeatPlayers[player.userID]);
                SeatPlayers.Remove(player.userID);
            }
            if(JoinedViaNexus.Contains(player))
            {
                DirectMessage(player, config._StatusSettings.ArriveMSG);
                JoinedViaNexus.Remove(player);
            }
            //Remove transfere protection
            player.SetFlag(BaseEntity.Flags.Protected, false);
        }

        private object CanHelicopterTarget(PatrolHelicopterAI heli, BasePlayer player){return FerryProtection(player);}
        private object CanHelicopterStrafeTarget(PatrolHelicopterAI heli, BasePlayer player){return FerryProtection(player);}
        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info){return FerryProtection(entity);}

        private void Unload()
        {
            //Remove plugin created stuff.
            foreach (BaseNetworkable basenetworkable in unloadable)
            {
                //Destroy island
                if (basenetworkable.prefabID == 2795004596) { basenetworkable.Kill(); }
                //Distroy ferry
                if (basenetworkable.prefabID == 2508295857)
                {
                    //Eject any entitys on the ferry so they dont drop in water
                    OpenNexusFerry Ferry = basenetworkable as OpenNexusFerry;
                    if (Ferry != null)
                    {
                        //Dissconnect database
                        if (sqlConnection != null && sqlConnection.Con != null) { sqlLibrary.CloseDb(sqlConnection); }
                        //Updates ferry contents list and ejects it to dock.
                        Ferry.UpdateDockedEntitys();
                        EjectEntitys(Ferry.GetFerryContents(), Ferry.DockedEntitys, Ferry.EjectionZone.position);
                        //Destroys Transform objects
                        UnityEngine.Object.Destroy(Ferry.Arrival.gameObject);
                        UnityEngine.Object.Destroy(Ferry.CastingOff.gameObject);
                        UnityEngine.Object.Destroy(Ferry.Departure.gameObject);
                        UnityEngine.Object.Destroy(Ferry.Docked.gameObject);
                        UnityEngine.Object.Destroy(Ferry.Docking.gameObject);
                        UnityEngine.Object.Destroy(Ferry.EjectionZone.gameObject);
                        Ferry.Kill();
                    }
                }
            }
            //Edge teleport
            if (config._ServerSettings.EdgeTeleporter)
            {
                foreach (BaseNetworkable vehicle in BaseNetworkable.serverEntities.ToArray())
                {
                    if (vehicle is BaseVehicle)
                    {
                        EdgeTeleport et = vehicle.GetComponent<EdgeTeleport>();
                        if (et != null) { UnityEngine.Object.Destroy(et); }
                    }
                }
            }
            //Remove CUI messaghes
            foreach (BasePlayer player in BasePlayer.activePlayerList){CuiHelper.DestroyUi(player, "FerryInfo");}
            config = null;
            plugin = null;
        }
        #endregion

        #region Startup
        private void Fstartup()
        {
            //Waits for fully loaded before running
            timer.Once(10f, () =>
            {
                //Still starting so run a timer again in 10 sec to check.
                try{if (Rust.Application.isLoading) { Fstartup(); return; }}catch { }
                //Starup script now.
                Startup();
            });
        }

        //Set IP and port of ferry from prefab name
        private void SetFerryIPs(OpenNexusFerry OpenFerry, string[] FerrySettings)
        {
            foreach (string setting in FerrySettings)
            {
                string[] tempsetting = setting.Split(':');
                foreach (string finalsetting in tempsetting)
                {
                    string settingparsed = finalsetting.ToLower().Replace(":", "");
                    if (settingparsed.Contains("server=")){OpenFerry.ServerIP = settingparsed.Replace("server=", "");}
                    else if (settingparsed.Contains("port=")){OpenFerry.ServerPort = settingparsed.Replace("port=", "");}
                }
            }
        }

        private void Startup()
        {
            //Check avaliable plugins that are supported
            if (Backpacks == null) { Puts("Backpacks plugin supported https://github.com/LaserHydra/Backpacks"); }
            if (Economics == null) { Puts("Economics supported https://umod.org/plugins/economics"); }
            if (ZLevelsRemastered == null) { Puts("ZLevels Remastered supported https://umod.org/plugins/zlevels-remastered"); }
            if (ServerRewards == null) { Puts("ServerRewards supported https://umod.org/plugins/server-rewards"); }
            //Find this servers IP and Port if not manually set
            if (thisserverip == "") thisserverip = covalence.Server.Address.ToString();
            if (thisserverport == "") thisserverport = covalence.Server.Port.ToString();
            //Scan map prefabs
            foreach (PrefabData prefabdata in World.Serialization.world.prefabs)
            {
                //Find Nexus dock
                if (prefabdata.id == 1548681896)
                {
                    //Read settings from prefab name
                    string[] FerrySettings = prefabdata.category.Replace(@"\", "").Split(',');
                    if (FerrySettings == null || FerrySettings.Length < 2) { Debug.LogError("OpenNexus Dock not setup properly"); continue; }
                    //Create rotation/position data
                    Quaternion rotation = Quaternion.Euler(new Vector3(prefabdata.rotation.x, prefabdata.rotation.y, prefabdata.rotation.z));
                    Vector3 position = new Vector3(prefabdata.position.x, prefabdata.position.y, prefabdata.position.z);
                    //Adjust docked position for ferry
                    position += (rotation * Vector3.forward) * 30.5f;
                    position += (rotation * Vector3.right) * -10f;
                    position += (rotation * Vector3.up) * 0.5f;
                    //Create New Ferry
                    NexusFerry ferry = (NexusFerry)GameManager.server.CreateEntity(StringPool.Get(2508295857), position, rotation) as NexusFerry;
                    if (ferry == null) continue;
                    //Attach open nexus code
                    OpenNexusFerry OpenFerry = ferry.gameObject.AddComponent<OpenNexusFerry>();
                    if (OpenFerry == null) continue;
                    //Setup with setting in dock prefab name
                    SetFerryIPs(OpenFerry, FerrySettings);
                    //Finish creating OpenNexus Ferrys
                    OpenFerry.prefabID = ferry.prefabID;
                    OpenFerry.syncPosition = true;
                    OpenFerry.globalBroadcast = true;
                    UnityEngine.Object.DestroyImmediate(ferry);
                    OpenFerry.enableSaving = false;
                    OpenFerry.Spawn();
                    unloadable.Add(OpenFerry);
                }
                //Find NexusIslands
                if (prefabdata.id == 2795004596)
                {
                    //Create rotation/position data
                    Quaternion rotation = Quaternion.Euler(new Vector3(prefabdata.rotation.x, prefabdata.rotation.y, prefabdata.rotation.z));
                    Vector3 position = new Vector3(prefabdata.position.x, prefabdata.position.y, prefabdata.position.z);
                    NexusIsland island = GameManager.server.CreateEntity(StringPool.Get(2795004596), position, rotation) as NexusIsland;
                    if (island == null) continue;
                    OpenNexusIsland openNexusIsland = island.gameObject.AddComponent<OpenNexusIsland>();
                    openNexusIsland.prefabID = island.prefabID;
                    openNexusIsland.syncPosition = true;
                    openNexusIsland.globalBroadcast = true;
                    UnityEngine.Object.DestroyImmediate(island);
                    openNexusIsland.enableSaving = false;
                    openNexusIsland.Spawn();
                    IslandData id = new IslandData();
                    id.Island = openNexusIsland;
                    id.location = position;
                    id.rotation = rotation;
                    FoundIslands.Add(id);
                    Puts("Found Island @ " + position.ToString());
                    unloadable.Add(openNexusIsland);
                }
            }
            //Remove dead Ferry turrets at junk collection (happens on reboots might shoot though low ground maps)
            List<NPCAutoTurret> Z = new List<NPCAutoTurret>();
            Vis.Entities<NPCAutoTurret>(Vector3.zero, 25f, Z);
            foreach (NPCAutoTurret ss in Z) { ss.Kill(); }
            //Add edge teleport
            if (config._ServerSettings.EdgeTeleporter) { foreach (BaseNetworkable vehicle in BaseNetworkable.serverEntities) { if (vehicle is BaseVehicle && vehicle.GetComponent<EdgeTeleport>() == null) vehicle.gameObject.AddComponent<EdgeTeleport>(); } }
        }
        #endregion

        #region functions
        private void AdjustConnectionScreen(BasePlayer player, string msg, int wait) { ServerMgr.Instance.connectionQueue.nextMessageTime = 1; if (Net.sv.write.Start()) { Net.sv.write.PacketID(Message.Type.Message); Net.sv.write.String(msg); Net.sv.write.String("Please wait " + wait + " seconds"); Net.sv.write.Send(new SendInfo(player.Connection)); } }
        private Vector3 StringToVector3(string sVector) { if (sVector.StartsWith("(") && sVector.EndsWith(")")) { sVector = sVector.Substring(1, sVector.Length - 2); } string[] sArray = sVector.Split(','); Vector3 result = new Vector3(float.Parse(sArray[0]), float.Parse(sArray[1]), float.Parse(sArray[2])); return result; }
        private Quaternion StringToQuaternion(string sVector) { if (sVector.StartsWith("(") && sVector.EndsWith(")")) { sVector = sVector.Substring(1, sVector.Length - 2); } string[] sArray = sVector.Split(','); Quaternion result = new Quaternion(float.Parse(sArray[0]), float.Parse(sArray[1]), float.Parse(sArray[2]), float.Parse(sArray[3])); return result; }
        private void StartSleeping(BasePlayer player) { if (!player.IsSleeping()) { Interface.CallHook("OnPlayerSleep", player); player.SetPlayerFlag(BasePlayer.PlayerFlags.Sleeping, true); player.sleepStartTime = Time.time; BasePlayer.sleepingPlayerList.Add(player); player.CancelInvoke("InventoryUpdate"); player.CancelInvoke("TeamUpdate"); player.SendNetworkUpdateImmediate(); } }
        private bool HasPermission(BasePlayer player, string perm) => permission.UserHasPermission(player.UserIDString, perm);
        private bool IsSteamId(string id) { ulong userId; if (!ulong.TryParse(id, out userId)) { return false; } return userId > 76561197960265728L; }
        private void DestroyMeshCollider(BaseEntity ent) { foreach (var mesh in ent.GetComponentsInChildren<MeshCollider>()) { UnityEngine.Object.DestroyImmediate(mesh); } }
        private void DestroyGroundComp(BaseEntity ent)
        {
            foreach (var groundmissing in ent.GetComponentsInChildren<DestroyOnGroundMissing>().ToArray()) { UnityEngine.Object.DestroyImmediate(groundmissing); }
            foreach (var groundwatch in ent.GetComponentsInChildren<GroundWatch>().ToArray()) { UnityEngine.Object.DestroyImmediate(groundwatch); }
        }

        private object FerryProtection(BaseEntity be)
        {
            BaseEntity Ferry = be.GetParentEntity();
            if (Ferry != null && Ferry is OpenNexusFerry) { return false; }
            return null;
        }

        private void MessageScreen(string msg, Vector3 pos, float radius, int delay = 8)
        {
            if (config._StatusSettings.StatusMsg == false) { return; }
            List<BasePlayer> PlayersInRange = new List<BasePlayer>();
            //Scans area for players
            Vis.Entities<BasePlayer>(pos, radius, PlayersInRange);
            //Shows CUI to each player in range
            if (PlayersInRange.Count != 0){foreach (BasePlayer player in PlayersInRange.ToArray()){if (!player.IsSleeping()){DirectMessage(player, msg, delay);}}}
        }

        private void DirectMessage(BasePlayer player,string msg, int delay = 8)
        {
            CuiHelper.DestroyUi(player, "FerryInfo");
            var elements = new CuiElementContainer();
            elements.Add(new CuiLabel { Text = { Text = msg, FontSize = config._StatusSettings.FontSize, Align = TextAnchor.MiddleCenter, FadeIn = config._StatusSettings.FontFadeIn, Color = config._StatusSettings.FontColour }, RectTransform = { AnchorMin = config._StatusSettings.AnchorMin, AnchorMax = config._StatusSettings.AnchorMax } }, "Overlay", "FerryInfo");
            CuiHelper.AddUi(player, elements);
            //Destroys message after delay
            timer.Once(delay, () =>{CuiHelper.DestroyUi(player, "FerryInfo");});
        }

        private void AddLock(BaseEntity ent, string data)
        {
            //Recreates codelocks
            string[] codedata = data.Split(new string[] { "<CodeLock>" }, System.StringSplitOptions.RemoveEmptyEntries);
            string[] wlp = codedata[4].Split(new string[] { "<player>" }, System.StringSplitOptions.RemoveEmptyEntries);
            CodeLock alock = GameManager.server.CreateEntity("assets/prefabs/locks/keypad/lock.code.prefab") as CodeLock;
            alock.Spawn();
            foreach (string wl in wlp) { alock.whitelistPlayers.Add(ulong.Parse(wl)); }
            try { alock.OwnerID = alock.whitelistPlayers[0]; } catch { }
            alock.code = codedata[2];
            alock.SetParent(ent, ent.GetSlotAnchorName(BaseEntity.Slot.Lock));
            alock.transform.localPosition = StringToVector3(codedata[0]);
            alock.transform.localRotation = StringToQuaternion(codedata[1]);
            ent.SetSlot(BaseEntity.Slot.Lock, alock);
            alock.SetFlag(BaseEntity.Flags.Locked, bool.Parse(codedata[3]));
            alock.enableSaving = true;
            alock.SendNetworkUpdateImmediate(true);
        }

        private void MountPlayer(BasePlayer player, int seatnum)
        {
            //Try mount seat given in setting
            List<BaseVehicle> bv = new List<BaseVehicle>();
            Vis.Entities<BaseVehicle>(player.transform.position, 1f, bv);
            try
            {
                foreach (BaseVehicle seat in bv)
                {
                    if(!seat.GetMountPoint(seatnum).mountable.HasValidDismountPosition(player))
                    {
                        player.Teleport(player.transform.position += new Vector3(0, 3, 0));
                        return;
                    }
                    seat.GetMountPoint(seatnum).mountable.MountPlayer(player);
                    return;
                }
            }
            catch { }
            //Fall back to find nearest seat and mount
            List<BaseMountable> Seats = new List<BaseMountable>();
            Vis.Entities<BaseMountable>(player.transform.position, 0.5f, Seats);
            BaseMountable closest_seat = null;
            foreach (BaseMountable seat in Seats)
            {
                if (seat.HasFlag(BaseEntity.Flags.Busy)) continue;
                if (closest_seat == null) closest_seat = seat;
                if (Vector3.Distance(player.transform.position, seat.transform.position) <= Vector3.Distance(player.transform.position, closest_seat.transform.position))
                    closest_seat = seat;
            }
            //Trys to mount seat
            if (closest_seat != null)
            {
                BaseMountable seat = closest_seat.GetComponent<BaseMountable>();
                if (seat == null || !seat.HasValidDismountPosition(player))
                {
                    player.Teleport(player.transform.position += new Vector3(0, 3, 0));
                    return;
                }
                seat.MountPlayer(player);
                closest_seat.SendNetworkUpdateImmediate();
                player.SendNetworkUpdateImmediate();
            }
        }

        private bool TryFindEjectionPosition(out Vector3 position, Vector3 spawnpos, float radius = 1f)
        {
            //try 100 times or drop on center
            Vector3 position2;
            Vector3 position3;
            for (int i = 0; i < 100; i++)
            {
                position2 = new Vector3(Core.Random.Range(spawnpos.x - 10f, spawnpos.x + 10f), spawnpos.y + 2.5f, Core.Random.Range(spawnpos.z - 10f, spawnpos.z + 10f));
                if (!TransformUtil.GetGroundInfo(position2, out position2, out position3, 10, 413204481, null))
                {
                    position = spawnpos + new Vector3(0, 5, 0);
                    return false;
                }
                if (GamePhysics.CheckSphere(position2, radius, Layers.Mask.Construction | Layers.Server.Deployed | Layers.World | Layers.Server.Players | Layers.Mask.Vehicle_World | Layers.Server.VehiclesSimple | Layers.Server.NPCs, QueryTriggerInteraction.Ignore)) { continue; }
                position = position2;
                return true;
            }
            position = spawnpos + new Vector3(0, 5, 0);
            return false;
        }

        private void EjectEntitys(List<BaseNetworkable> currentcontents, List<BaseNetworkable> DockedEntitys, Vector3 EjectionZone)
        {
            //Ejects anything left on Ferry to dock.
            if (currentcontents == null || currentcontents.Count == 0 || DockedEntitys == null || DockedEntitys.Count == 0) { return; }
            foreach (BaseEntity entity in currentcontents.ToArray())
            {
                if (entity != null && DockedEntitys.Contains(entity))
                {
                    //Remove item boxes that might end up on ferry when despawning things
                    if (entity.ToString().Contains("item_drop"))
                    {
                        entity.Kill();
                        continue;
                    }
                    Vector3 serverPosition;
                    if (plugin.TryFindEjectionPosition(out serverPosition, EjectionZone))
                    {
                        if (entity is BasePlayer)
                        {
                            BasePlayer player = entity as BasePlayer;
                            try
                            {
                                if (player.IsConnected)
                                {
                                    player.EnsureDismounted();
                                    if (player.HasParent()) { player.SetParent(null, true, true); }
                                    player.RemoveFromTriggers();
                                    player.SetServerFall(true);
                                    player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
                                }
                                player.Teleport(serverPosition);
                                if (player.IsConnected){player.SendEntityUpdate();}
                                player.UpdateNetworkGroup();
                                player.SendNetworkUpdateImmediate(false);
                            }
                            finally
                            {
                                player.SetServerFall(false);
                                player.ForceUpdateTriggers();
                            }
                        }
                        else
                        {
                            entity.SetParent(null, false, false);
                            entity.ServerPosition = serverPosition;
                            entity.SendNetworkUpdateImmediate(false);
                        }
                        continue;
                    }
                }
            }
            DockedEntitys.Clear();
        }
        #endregion

        #region DatabaseFunctions
        //Build database of current time/weather
        private string getclimate() { return (TOD_Sky.Instance.Cycle.Year) + "|" + (TOD_Sky.Instance.Cycle.Month) + "|" + (TOD_Sky.Instance.Cycle.Day) + "|" + (TOD_Sky.Instance.Cycle.Hour) + "|" + (climate.WeatherState.Atmosphere.Brightness.ToString()) + "|" + (climate.WeatherState.Atmosphere.Contrast.ToString()) + "|" + (climate.WeatherState.Atmosphere.Directionality.ToString()) + "|" + (climate.WeatherState.Atmosphere.MieMultiplier.ToString()) + "|" + (climate.WeatherState.Atmosphere.RayleighMultiplier.ToString()) + "|" + (climate.WeatherState.Clouds.Attenuation.ToString()) + "|" + (climate.WeatherState.Clouds.Brightness.ToString()) + "|" + (climate.WeatherState.Clouds.Coloring.ToString()) + "|" + (climate.WeatherState.Clouds.Coverage.ToString()) + "|" + (climate.WeatherState.Clouds.Opacity.ToString()) + "|" + (climate.WeatherState.Clouds.Saturation.ToString()) + "|" + (climate.WeatherState.Clouds.Scattering.ToString()) + "|" + (climate.WeatherState.Clouds.Sharpness.ToString()) + "|" + (climate.WeatherState.Clouds.Size.ToString()) + "|" + (climate.Weather.DustChance.ToString()) + "|" + (climate.WeatherState.Atmosphere.Fogginess.ToString()) + "|" + (climate.Weather.FogChance.ToString()) + "|" + (climate.Weather.OvercastChance.ToString()) + "|" + (climate.WeatherState.Rain.ToString()) + "|" + (climate.Weather.RainChance.ToString()) + "|" + (climate.WeatherState.Rainbow.ToString()) + "|" + (climate.Weather.StormChance.ToString()) + "|" + (climate.WeatherState.Thunder.ToString()) + "|" + (climate.WeatherState.Wind.ToString()) + "|"; }

        private void setclimate()
        {
            //Load from database time/weather from the first server in list.
            string sqlQuery = "SELECT `sender`, `climate` FROM sync LIMIT 1";
            Sql selectCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery);
            sqlLibrary.Query(selectCommand, sqlConnection, list =>
            {
                if (list == null) { return; }
                foreach (Dictionary<string, object> entry in list)
                {
                    if (entry["sender"].ToString() == thisserverip + ":" + thisserverport) { if (config._ServerSettings.ShowDebugMsg) Puts("Dont Sync Weather/Time this is first server"); return; }
                    string[] settings = entry["climate"].ToString().Split('|');
                    TOD_Sky.Instance.Cycle.Year = int.Parse(settings[0]);
                    TOD_Sky.Instance.Cycle.Month = int.Parse(settings[1]);
                    TOD_Sky.Instance.Cycle.Day = int.Parse(settings[2]);
                    TOD_Sky.Instance.Cycle.Hour = float.Parse(settings[3]);
                    climate.WeatherState.Atmosphere.Brightness = float.Parse(settings[4]);
                    climate.WeatherState.Atmosphere.Contrast = float.Parse(settings[5]);
                    climate.WeatherState.Atmosphere.Directionality = float.Parse(settings[6]);
                    climate.WeatherState.Atmosphere.MieMultiplier = float.Parse(settings[7]);
                    climate.WeatherState.Atmosphere.RayleighMultiplier = float.Parse(settings[8]);
                    climate.WeatherState.Clouds.Attenuation = float.Parse(settings[9]);
                    climate.WeatherState.Clouds.Brightness = float.Parse(settings[10]);
                    climate.WeatherState.Clouds.Coloring = float.Parse(settings[11]);
                    climate.WeatherState.Clouds.Coverage = float.Parse(settings[12]);
                    climate.WeatherState.Clouds.Opacity = float.Parse(settings[13]);
                    climate.WeatherState.Clouds.Saturation = float.Parse(settings[14]);
                    climate.WeatherState.Clouds.Scattering = float.Parse(settings[15]);
                    climate.WeatherState.Clouds.Sharpness = float.Parse(settings[16]);
                    climate.WeatherState.Clouds.Size = float.Parse(settings[17]);
                    climate.Weather.DustChance = float.Parse(settings[18]);
                    climate.WeatherState.Atmosphere.Fogginess = float.Parse(settings[19]);
                    climate.Weather.FogChance = float.Parse(settings[20]);
                    climate.Weather.OvercastChance = float.Parse(settings[21]);
                    climate.WeatherState.Rain = float.Parse(settings[22]);
                    climate.Weather.RainChance = float.Parse(settings[23]);
                    climate.WeatherState.Rainbow = float.Parse(settings[24]);
                    climate.Weather.StormChance = float.Parse(settings[25]);
                    climate.WeatherState.Thunder = float.Parse(settings[26]);
                    climate.WeatherState.Wind = float.Parse(settings[27]);
                    if (config._ServerSettings.ShowDebugMsg) Puts("Synced Weather/Time with first server");
                    return;
                }
            });
        }

        //Create team info
        private string getteams(BasePlayer player)
        {
            string teams = "";
            if (player != null)
            {
                foreach (var playerTeam in RelationshipManager.ServerInstance.teams.Values)
                {
                    if (playerTeam.teamID == 0UL || !playerTeam.teamLeader.IsSteamId() || playerTeam.members.IsNullOrEmpty()) continue;
                    if (playerTeam.members.Contains(player.userID))
                    {
                        teams += playerTeam.teamLeader.ToString() + ",";
                        foreach (ulong p in playerTeam.members){try { teams += p.ToString() + "<DN>" + BasePlayer.FindAwakeOrSleeping(p.ToString()).displayName + ","; } catch { }}
                    }
                }
            }
            return teams;
        }

        //Restore team info
        private void setteams(BasePlayer player, string teamdata)
        {
            if (teamdata == null || teamdata == "") { return; }
            List<ulong> playersinteams = new List<ulong>();
            bool alreadyinteam = false;
            foreach (var playerTeam in RelationshipManager.ServerInstance.teams.Values)
            {
                foreach (ulong p in playerTeam.members) { playersinteams.Add(p); }
                if (playerTeam.members.Contains(player.userID)) { alreadyinteam = true; }
            }
            if (alreadyinteam) { return; }
            string[] teams = teamdata.Split(',');
            ulong _ulong;
            Unsubscribe("OnTeamCreated");
            RelationshipManager.PlayerTeam aTeam = RelationshipManager.ServerInstance.CreateTeam();
            if (ulong.TryParse(teams[0], out _ulong))
            {
                if (playersinteams.Contains(_ulong)) { aTeam.teamLeader = player.userID; }
                else { aTeam.teamLeader = _ulong; }
                for (int i = 1; i < teams.Length - 1; i++)
                {
                    string[] userdetails = teams[i].Split(new string[] { "<DN>" }, StringSplitOptions.None);
                    if (!ulong.TryParse(userdetails[0], out _ulong)) { continue; }
                    if (playersinteams.Contains(_ulong)) { Puts("Player already in another team"); continue; }
                    playersinteams.Add(_ulong);
                    if (_ulong == player.userID) { aTeam.AddPlayer(player); }
                    else
                    {
                        BasePlayer bp = BasePlayer.FindAwakeOrSleeping(_ulong.ToString());
                        if (bp == null)
                        {
                            bp = GameManager.server.CreateEntity("assets/prefabs/player/player.prefab").ToPlayer();
                            bp.lifestate = BaseCombatEntity.LifeState.Dead;
                            bp.ResetLifeStateOnSpawn = true;
                            bp.SetFlag(BaseEntity.Flags.Protected, true);
                            bp.Spawn();
                            StartSleeping(bp);
                            bp.CancelInvoke(player.KillMessage);
                            bp.userID = _ulong;
                            bp.UserIDString = _ulong.ToString();
                            bp.displayName = userdetails[1];
                            bp.eyeHistory.Clear();
                            bp.lastTickTime = 0f;
                            bp.lastInputTime = 0f;
                            bp.stats.Init();
                        }
                        if (bp != null)
                        {
                            aTeam.AddPlayer(bp);
                            playersinteams.Add(_ulong);
                        }
                    }
                }
            }
            Subscribe("OnTeamCreated");
        }

        private string getmodifiers(BasePlayer player)
        {

            //Build database of players current modifiers
            string mods = "";
            if (player != null && config._PluginSettings.Modifiers == true) { foreach (Modifier m in player.modifiers.All) { mods += (m.Type.ToString()) + "," + (m.Source.ToString()) + "," + (m.Value.ToString()) + "," + (m.Duration.ToString()) + "," + (m.TimeRemaining.ToString()) + "<MF>"; } }
            return mods;
        }

        private void setmodifiers(BasePlayer player, string[] mods)
        {
            //Read from database players modifiers
            if (player != null && mods != null && mods.Length != 0 && config._PluginSettings.Modifiers == true)
            {
                List<ModifierDefintion> md = new List<ModifierDefintion>();
                foreach (string mod in mods)
                {
                    if (mod == null || mod == "" || !mod.Contains(",")) { continue; }
                    string[] m = mod.Split(',');
                    if (m.Length == 5)
                    {
                        ModifierDefintion moddef = new ModifierDefintion();
                        switch (m[0])
                        {
                            case "Wood_Yield":
                                moddef.type = Modifier.ModifierType.Wood_Yield;
                                break;
                            case "Ore_Yield":
                                moddef.type = Modifier.ModifierType.Ore_Yield;
                                break;
                            case "Radiation_Resistance":
                                moddef.type = Modifier.ModifierType.Radiation_Resistance;
                                break;
                            case "Radiation_Exposure_Resistance":
                                moddef.type = Modifier.ModifierType.Radiation_Exposure_Resistance;
                                break;
                            case "Max_Health":
                                moddef.type = Modifier.ModifierType.Max_Health;
                                break;
                            case "Scrap_Yield":
                                moddef.type = Modifier.ModifierType.Scrap_Yield;
                                break;
                        }
                        moddef.source = Modifier.ModifierSource.Tea;
                        float _float;
                        if (float.TryParse(m[2], out _float)) { moddef.value = _float; }
                        if (float.TryParse(m[4], out _float)) { moddef.duration = _float; }
                        md.Add(moddef);
                    }
                }
                player.modifiers.Add(md);
                player.DirtyPlayerState();
            }
        }

        private string getblueprints(BasePlayer player)
        {
            if (config._PluginSettings.BluePrints == false) return "";
            //Build data base of players unloacked blueprints
            string bps = "";
            if (player != null) { foreach (var blueprint in player.PersistantPlayerInfo.unlockedItems) { bps += blueprint + "<BP>"; } }
            return bps;
        }

        private void setblueprints(BasePlayer player, string[] blueprints)
        {
            //Apply unlocked blueprints to player from database
            if (player != null && blueprints != null && blueprints.Length != 0 && config._PluginSettings.BluePrints == true)
            {
                foreach (string blueprint in blueprints)
                {
                    if (blueprint == null || blueprint == "") { continue; }
                    int bp;
                    bool success = int.TryParse(blueprint, out bp);
                    if (success){if (!player.PersistantPlayerInfo.unlockedItems.Contains(bp)){player.PersistantPlayerInfo.unlockedItems.Add(bp);}}
                }
                player.SendNetworkUpdateImmediate();
                player.ClientRPCPlayer(null, player, "UnlockedBlueprint", 0);
            }
        }

        private void SetupFuel(BaseVehicle target, int amount)
        {
            //Apply fuel ammount
            var fuelContainer = target?.GetFuelSystem()?.GetFuelContainer()?.inventory;
            if (fuelContainer != null)
            {
                Item lowgrade = ItemManager.CreateByItemID(-946369541, amount);
                if (!lowgrade.MoveToContainer(fuelContainer)) { lowgrade.Remove(); }
            }
        }

        private void ApplySettings(BaseVehicle bv, BaseEntity parent, float health, float maxhealth, int fuel, ulong ownerid, Vector3 pos, Quaternion rot)
        {
            //Apply base settings
            bv.SetMaxHealth(maxhealth);
            bv.health = health;
            if (fuel != 0) { SetupFuel(bv, fuel); }
            bv.OwnerID = ownerid;
            if (parent is OpenNexusFerry)
            {
                bv.SetParent(parent);
                bv.transform.localPosition = pos;
                bv.transform.localRotation = rot;
            }
            else
            {
                Vector3 PastePosition;
                TryFindEjectionPosition(out PastePosition, parent.transform.position, 1);
                bv.transform.position = PastePosition;
            }
            bv.TransformChanged();
            bv.SendNetworkUpdateImmediate();
        }

        private void MoveItems(BaseSettings settings, ItemContainer Inventory)
        {
            //trys to move into container position other wise force inserts into position
            if (settings == null || Inventory == null) return;
            Item item = CreateItem(settings.id, settings.amount, settings.skinid, settings.condition, settings.code, settings.imgdata, settings.oggdata, settings.text);
            if (item != null)
            {
                if (!item.MoveToContainer(Inventory, settings.slot, true, true))
                {
                    item.position = settings.slot;
                    Inventory.Insert(item);
                }
            }
        }
        #endregion

        #region DataProcessing
        //Process data packet
        private Dictionary<string, BaseNetworkable> ReadPacket(string packet, BaseEntity parent, int id)
        {
            //List of entitys that have been recreated
            Dictionary<string, BaseNetworkable> CreatedEntitys = new Dictionary<string, BaseNetworkable>();
            //Checks its a opennexus packet
            if (packet.Contains("<OpenNexus>"))
            {
                object injection = Interface.CallHook("OnOpenNexusRead", packet);
                if (injection is string) { packet = injection as string; }
                else if (injection is bool) { return null; }
                //Mark Packet as read
                MySQLWrite("", "", "", id, 1);
                BasePlayer admin = null;
                if (parent is BasePlayer) { admin = parent as BasePlayer; }
                //Hold variables for cars
                Dictionary<string, ModularCar> SpawnedCars = new Dictionary<string, ModularCar>();
                //Deserilize from webpacket
                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, List<Dictionary<string, string>>>>(packet.Replace("<OpenNexus>", ""));
                foreach (KeyValuePair<string, List<Dictionary<string, string>>> packets in data)
                {
                    if (admin == null)
                    {
                        //Process Packets
                        if (packets.Key.Contains("BasePlayerBackpackData"))
                        {
                            SetBackpacksData(packets.Key, packets.Value);
                            continue;
                        }
                        if (packets.Key.Contains("BasePlayerEconomicsData"))
                        {
                            foreach (Dictionary<string, string> i in packets.Value) { foreach (KeyValuePair<string, string> ii in i) { SetEconomicsData(ii.Key, ii.Value); } }
                            continue;
                        }
                        if (packets.Key.Contains("BasePlayerServerRewardsData"))
                        {
                            foreach (Dictionary<string, string> i in packets.Value) { foreach (KeyValuePair<string, string> ii in i) { SetServerRewardsData(ii.Key, ii.Value); } }
                            continue;
                        }
                        if (packets.Key.Contains("BasePlayerZLevelsRemasteredData"))
                        {
                            foreach (Dictionary<string, string> i in packets.Value) { foreach (KeyValuePair<string, string> ii in i) { SetZLevelsRemasteredData(ii.Key, ii.Value); } }
                            continue;
                        }
                        if (packets.Key.Contains("BasePlayerInventory"))
                        {
                            BaseSettings settings = new BaseSettings();
                            settings.ProcessPacket(packets.Value, null, 3, null, CreatedEntitys);
                            continue;
                        }
                        if (packets.Key.Contains("BasePlayer"))
                        {
                            var bp = ProcessBasePlayer(packets.Value, parent);
                            if (bp != null) { foreach (KeyValuePair<string, BaseNetworkable> m in bp) { CreatedEntitys.Add(m.Key, m.Value); } }
                            continue;
                        }
                    }
                    if (packets.Key.Contains("MiniCopter"))
                    {
                        var mc = ProcessHeli(packets.Value, parent);
                        if (mc != null) { foreach (KeyValuePair<string, BaseNetworkable> m in mc) { CreatedEntitys.Add(m.Key, m.Value); } }
                        continue;
                    }
                    if (packets.Key.Contains("BaseBoat"))
                    {
                        var bb = ProcessBoat(packets.Value, parent);
                        if (bb != null) { foreach (KeyValuePair<string, BaseNetworkable> m in bb) { CreatedEntitys.Add(m.Key, m.Value); } }
                        continue;
                    }
                    if (packets.Key.Contains("BaseCrane"))
                    {
                        var bc = ProcessCrane(packets.Value, parent);
                        if (bc != null) { foreach (KeyValuePair<string, BaseNetworkable> m in bc) { CreatedEntitys.Add(m.Key, m.Value); } }
                        continue;
                    }
                    if (packets.Key.Contains("SnowMobile"))
                    {
                        var bc = ProcessSnowmobile(packets.Value, parent);
                        if (bc != null) { foreach (KeyValuePair<string, BaseNetworkable> m in bc) { CreatedEntitys.Add(m.Key, m.Value); } }
                        continue;
                    }
                    if (packets.Key.Contains("BaseSubmarine"))
                    {
                        var bs = ProcessSub(packets.Value, parent);
                        if (bs != null) { foreach (KeyValuePair<string, BaseNetworkable> m in bs) { CreatedEntitys.Add(m.Key, m.Value); } }
                        continue;
                    }
                    if (packets.Key.Contains("BaseHorseInventory"))
                    {
                        BaseSettings settings = new BaseSettings();
                        settings.ProcessPacket(packets.Value, null, 3, null, CreatedEntitys);
                        continue;
                    }
                    if (packets.Key.Contains("BaseHorse"))
                    {
                        var rh = ProcessHorse(packets.Value, parent);
                        if (rh != null) { foreach (KeyValuePair<string, BaseNetworkable> r in rh) { CreatedEntitys.Add(r.Key, r.Value); } }
                        continue;
                    }
                    if (packets.Key.Contains("ModularCarItems"))
                    {
                        //Delay in these ones to allow car to be spawned
                        NextTick(() =>
                        {
                            //Process module car parts
                            BaseSettings settings = new BaseSettings();
                            settings.ProcessPacket(packets.Value, SpawnedCars, 1);
                        });
                        continue;
                    }
                    if (packets.Key.Contains("ModularCarCamper"))
                    {
                        NextTick(() =>
                        {
                            //Process camper module
                            BaseSettings settings = new BaseSettings();
                            settings.ProcessPacket(packets.Value, SpawnedCars, 0);
                        });
                        continue;
                    }
                    if (packets.Key.Contains("ModularCar"))
                    {
                        var sc = ProcessCar(packets.Value, parent);
                        if (sc != null) { foreach (KeyValuePair<string, ModularCar> c in sc) { SpawnedCars.Add(c.Key, c.Value); } }
                        continue;
                    }
                }
                //Max out retry counter to end retrying
                if (parent is OpenNexusFerry) { (parent as OpenNexusFerry).retrys = 999; }
            }
            //Return list of everything created.
            return CreatedEntitys;
        }

        private Dictionary<string, BaseNetworkable> ProcessHeli(List<Dictionary<string, string>> packets, BaseEntity parent)
        {
            //Process mini and scrap heli
            BaseSettings settings = new BaseSettings();
            settings.ProcessPacket(packets);
            //Create heli
            MiniCopter minicopter = GameManager.server.CreateEntity(settings.prefab) as MiniCopter;
            if (minicopter == null) return null;
            //spawn and setit up
            minicopter.Spawn();
            AttachFamily(minicopter, settings.children);
            if (settings.unlimitedfuel) { minicopter.fuelPerSec = 0; }
            ApplySettings(minicopter, parent, settings.health, settings.maxhealth, settings.fuel, settings.ownerid, settings.pos, settings.rot);
            return new Dictionary<string, BaseNetworkable>() { { settings.netid, minicopter } };
        }

        private Dictionary<string, BaseNetworkable> ProcessBoat(List<Dictionary<string, string>> packets, BaseEntity parent)
        {
            //Process boat and rhib
            BaseSettings settings = new BaseSettings();
            settings.ProcessPacket(packets);
            //Create boat
            BaseBoat boat = GameManager.server.CreateEntity(settings.prefab) as BaseBoat;
            if (boat == null) return null;
            //spawn and setit up
            boat.Spawn();
            AttachFamily(boat, settings.children);
            if (settings.unlimitedfuel) { (boat as MotorRowboat).fuelPerSec = 0; }
            ApplySettings(boat, parent, settings.health, settings.maxhealth, settings.fuel, settings.ownerid, settings.pos, settings.rot);
            return new Dictionary<string, BaseNetworkable>() { { settings.netid, boat } };
        }

        private Dictionary<string, BaseNetworkable> ProcessCrane(List<Dictionary<string, string>> packets, BaseEntity parent)
        {
            //Process crane
            BaseSettings settings = new BaseSettings();
            settings.ProcessPacket(packets);
            //Create crane
            BaseCrane crane = GameManager.server.CreateEntity(settings.prefab) as BaseCrane;
            if (crane == null) return null;
            //spawn and setit up
            crane.Spawn();
            AttachFamily(crane, settings.children);
            if (settings.unlimitedfuel) { crane.fuelPerSec = 0; }
            ApplySettings(crane, parent, settings.health, settings.maxhealth, settings.fuel, settings.ownerid, settings.pos, settings.rot);
            return new Dictionary<string, BaseNetworkable>() { { settings.netid, crane } };
        }
        private Dictionary<string, BaseNetworkable> ProcessSub(List<Dictionary<string, string>> packets, BaseEntity parent)
        {
            //Process subs
            BaseSettings settings = new BaseSettings();
            settings.ProcessPacket(packets);
            //Create sub
            BaseSubmarine sub = GameManager.server.CreateEntity(settings.prefab) as BaseSubmarine;
            if (sub == null) return null;
            //spawn and setit up
            sub.Spawn();
            AttachFamily(sub, settings.children);
            if (settings.unlimitedfuel) { sub.maxFuelPerSec = 0; sub.idleFuelPerSec = 0; }
            ApplySettings(sub, parent, settings.health, settings.maxhealth, settings.fuel, settings.ownerid, settings.pos, settings.rot);
            return new Dictionary<string, BaseNetworkable>() { { settings.netid, sub } };
        }

        private Dictionary<string, BaseNetworkable> ProcessSnowmobile(List<Dictionary<string, string>> packets, BaseEntity parent)
        {
            //Process snowmobile
            BaseSettings settings = new BaseSettings();
            settings.ProcessPacket(packets);
            //Create snowmobile
            Snowmobile sm = GameManager.server.CreateEntity(settings.prefab) as Snowmobile;
            if (sm == null) return null;
            //spawn and setit up
            sm.Spawn();
            AttachFamily(sm, settings.children);
            if (settings.unlimitedfuel) { sm.maxFuelPerSec = 0; sm.idleFuelPerSec = 0; }
            ApplySettings(sm, parent, settings.health, settings.maxhealth, settings.fuel, settings.ownerid, settings.pos, settings.rot);
            return new Dictionary<string, BaseNetworkable>() { { settings.netid, sm } };
        }

        private Dictionary<string, ModularCar> ProcessCar(List<Dictionary<string, string>> packets, BaseEntity parent)
        {
            //Process modular car
            BaseSettings settings = new BaseSettings();
            settings.ProcessPacket(packets);
            //Create car
            ModularCar car = GameManager.server.CreateEntity(settings.prefab) as ModularCar;
            if (car == null) return null;
            //Spawn custom modules
            car.spawnSettings.useSpawnSettings = true;
            //Read module data and get it ready
            AttacheModules(car, settings.modules, settings.conditions);
            car.Spawn();
            string[] Modconditions = settings.conditions.Split('|');
            ////Delay to allow modules to be spawned
            NextTick(() =>
            {
                //Apply custom modules health
                int cond = 0;
                foreach (BaseVehicleModule vm in car.AttachedModuleEntities)
                {
                    //Apply modules health
                    float health;
                    bool success = float.TryParse(Modconditions[cond++], out health);
                    if (success) { vm.health = health; } else { vm.health = 50f; }
                    //Apply unlimited fuel
                    VehicleModuleEngine vme = vm as VehicleModuleEngine;
                    if (settings.unlimitedfuel && vme != null) { vme.engine.idleFuelPerSec = 0; vme.engine.maxFuelPerSec = 0; }
                }
                if (car != null)
                {
                    List<SleepingBag> Bags = new List<SleepingBag>();
                    foreach (BaseEntity baseEntity in car.children)
                    {
                        if (baseEntity == null) continue;
                        foreach (BaseEntity baseEntity2 in baseEntity.children)
                        {
                            if (baseEntity2 == null) continue;
                            SleepingBag sleepingBagCamper;
                            if ((sleepingBagCamper = (baseEntity2 as SleepingBag)) != null)
                            {
                                Bags.Add(sleepingBagCamper);
                            }
                        }
                    }
                    //Fix for bags owners and name
                    if (Bags.Count != 0)
                    {
                        ulong _ulong;
                        string[] BagData = settings.bags.Split(new string[] { "<bag>" }, StringSplitOptions.None);
                        if (BagData != null && BagData.Length != 0)
                        {
                            int bagmodded = 0;
                            foreach (string bag in BagData)
                            {
                                string[] data = bag.Split(new string[] { "<uid>" }, StringSplitOptions.None);
                                if (data.Length == 2)
                                {
                                    if (Bags.Count > bagmodded)
                                    {
                                        if (ulong.TryParse(data[1], out _ulong)) { Bags[bagmodded].deployerUserID = _ulong; }
                                        Bags[bagmodded].niceName = data[0];
                                        Bags[bagmodded].SendNetworkUpdateImmediate();
                                        bagmodded++;
                                    }
                                }
                            }
                        }
                    }
                    //Relock car if it had a lock
                    if(settings.lockid != 0)
                    {
                        Puts("Locking car " + settings.lockid.ToString());
                        car.carLock.LockID = settings.lockid;
                        car.carLock.owner.SendNetworkUpdate();
                    }
                }
            });
            ApplySettings(car, parent, settings.health, settings.maxhealth, settings.fuel, settings.ownerid, settings.pos, settings.rot);
            return new Dictionary<string, ModularCar>() { { settings.netid, car } };
        }

        object OnVehicleModulesAssign(ModularCar car, ItemModVehicleModule[] modulePreset)
        {
            //Car is spawning with custom flag Check if there custom modules waiting
            if (CarModules != null && CarModules.Count > 0)
            {
                foreach (ModuleData md in CarModules.ToArray())
                {
                    //Attach each module
                    for (int i = 0; i < md.modules.Count; i++)
                    {
                        Rust.Modular.ItemModVehicleModule itemModVehicleModule = md.modules[i];
                        if (itemModVehicleModule != null && car.Inventory.SocketsAreFree(i, itemModVehicleModule.socketsTaken, null))
                        {
                            itemModVehicleModule.doNonUserSpawn = true;
                            Item item = ItemManager.Create(itemModVehicleModule.GetComponent<ItemDefinition>(), 1, 0UL);
                            item.condition = item.maxCondition * md.condition[i];
                            if (!car.TryAddModule(item)) { item.Remove(0f); }
                        }
                    }
                    //remove from list since its done
                    CarModules.Remove(md);
                    //Call hook for any other plugins that use it.
                    Interface.CallHook("OnVehicleModulesAssigned", this, CarModules);
                    //Stop rest of default random spawn function.
                    return true;
                }
            }
            //Normal random modules spawn
            return null;
        }

        private void AttacheModules(ModularCar modularCar, string Modules, string Conditions)
        {
            //Seperate module settings from packet
            if (modularCar == null) return;
            string[] Modshortnames = Modules.Split('|');
            string[] Modconditions = Conditions.Split('|');
            if (Modshortnames != null && Modshortnames.Length != 0 && Modconditions != null && Modconditions.Length != 0)
            {
                int conditionslot = 0;
                ModuleData MD = new ModuleData();
                foreach (string shortname in Modshortnames)
                {
                    if (shortname != null && shortname != "")
                    {
                        //create modules
                        int cid;
                        float mcd;
                        bool success = int.TryParse(shortname, out cid);
                        bool success2 = float.TryParse(Modconditions[conditionslot++], out mcd);
                        if (!success || !success2) { continue; }
                        Item item = ItemManager.CreateByItemID(cid, 1, 0);
                        if (item == null) continue;
                        item.condition = mcd;
                        item.MarkDirty();
                        ItemModVehicleModule component = item.info.GetComponent<ItemModVehicleModule>();
                        if (component == null) continue;
                        MD.condition.Add(mcd);
                        MD.modules.Add(component);
                    }
                }
                CarModules.Add(MD);
            }
        }

        private Dictionary<string, BaseNetworkable> ProcessHorse(List<Dictionary<string, string>> packets, BaseEntity FerryPos)
        {
            //process horse
            BaseSettings settings = new BaseSettings();
            settings.ProcessPacket(packets);
            //create horse
            RidableHorse horse = GameManager.server.CreateEntity(settings.prefab) as RidableHorse;
            if (horse == null) return null;
            horse.Spawn();
            //set its health
            horse.SetMaxHealth(settings.maxhealth);
            horse.SetHealth(settings.health);
            //set its breed and stats
            horse.ApplyBreed(settings.currentBreed);
            horse.maxSpeed = settings.maxSpeed;
            horse.maxStaminaSeconds = settings.maxStaminaSeconds;
            horse.staminaSeconds = settings.maxStaminaSeconds;
            horse.staminaCoreSpeedBonus = settings.staminaCoreSpeedBonus;
            horse.OwnerID = settings.ownerid;
            horse.SetFlag(BaseEntity.Flags.Reserved4, bool.Parse(settings.flags[0]));
            horse.SetFlag(BaseEntity.Flags.Reserved5, bool.Parse(settings.flags[1]));
            horse.SetFlag(BaseEntity.Flags.Reserved6, bool.Parse(settings.flags[2]));
            if (FerryPos is OpenNexusFerry)
            {
                horse.SetParent(FerryPos);
                horse.transform.localPosition = settings.pos;
                horse.transform.localRotation = settings.rot;
            }
            else
            {
                Vector3 PastePosition;
                TryFindEjectionPosition(out PastePosition, FerryPos.transform.position, 1);
                horse.transform.position = PastePosition;
            }
            horse.TransformChanged();
            horse.SendNetworkUpdateImmediate();
            return new Dictionary<string, BaseNetworkable>() { { settings.netid, horse } };
        }

        private void ProcessCamper(Dictionary<string, ModularCar> SpawnedCars, BaseSettings settings)
        {
            if (SpawnedCars.ContainsKey(settings.ownerid.ToString()))
            {
                ModularCar mc = SpawnedCars[settings.ownerid.ToString()] as ModularCar;
                if (mc != null)
                {
                    foreach (var moduleEntity in mc.AttachedModuleEntities)
                    {
                        VehicleModuleCamper vehicleModuleCamper = moduleEntity as VehicleModuleCamper;
                        if (vehicleModuleCamper != null)
                        {
                            switch (settings.container)
                            {
                                case 0:
                                    //fill storage box
                                    MoveItems(settings, vehicleModuleCamper.activeStorage.Get(true).inventory);
                                    break;
                                case 1:
                                    //fill locker
                                    MoveItems(settings, vehicleModuleCamper.activeLocker.Get(true).inventory);
                                    break;
                                case 2:
                                    //Fill bbq
                                    MoveItems(settings, vehicleModuleCamper.activeBbq.Get(true).inventory);
                                    break;
                            }
                            //Update entitys
                            vehicleModuleCamper.SendNetworkUpdateImmediate();
                        }
                    }
                }
            }
        }


        private void ProcessModuleParts(Dictionary<string, ModularCar> SpawnedCars, BaseSettings settings)
        {
            if (SpawnedCars.ContainsKey(settings.ownerid.ToString()))
            {
                ModularCar mc = SpawnedCars[settings.ownerid.ToString()] as ModularCar;
                if (mc != null)
                {
                    int s = 0;
                    foreach (var moduleEntity in mc.AttachedModuleEntities)
                    {
                        if (s != settings.container) { s++; continue; }
                        VehicleModuleEngine vehicleModuleEngine = moduleEntity as VehicleModuleEngine;
                        if (vehicleModuleEngine != null)
                        {
                            MoveItems(settings, vehicleModuleEngine.GetContainer().inventory);
                            s++;
                            continue;
                        }
                        //Fill stoage with items
                        VehicleModuleStorage vehicleModuleStorage = moduleEntity as VehicleModuleStorage;
                        if (vehicleModuleStorage != null)
                        {
                            MoveItems(settings, vehicleModuleStorage.GetContainer().inventory);
                            s++;
                            continue;
                        }
                        s++;
                    }
                }
            }
        }

        private Dictionary<string, BaseNetworkable> ProcessBasePlayer(List<Dictionary<string, string>> packets, BaseEntity FerryPos)
        {
            //Process BasePlayer
            BaseSettings settings = new BaseSettings();
            settings.ProcessPacket(packets);
            if (IsSteamId(settings.steamid))
            {
                ulong steamid;
                bool success = ulong.TryParse(settings.steamid, out steamid);
                if (!success) { return null; }


                //Server Server for player already spawned;
                BasePlayer player = BasePlayer.FindAwakeOrSleeping(settings.steamid);
                if (player != null)
                {
                    //Drop all items on that player where they were asleep since shouldnt be a body when transfereing from ferry
                    if (player.inventory.AllItems().Length != 0)
                    {
                        foreach (Item item in player.inventory.AllItems()){item.DropAndTossUpwards(player.transform.position);}
                    }
                    if (player.IsConnected)
                    {
                        //Some how player is already on server so resync them to ferrys data.
                        plugin.AdjustConnectionScreen(player, "Open Nexus Resyncing Server", 0);
                        player.ClientRPCPlayer(null, player, "StartLoading");
                        ConsoleNetwork.SendClientCommand(player.net.connection, "nexus.redirect", new object[] { thisserverip, thisserverport });
                    }
                    player.Kill();
                }
                //Create new player for them to spawn into
                player = GameManager.server.CreateEntity("assets/prefabs/player/player.prefab", FerryPos.transform.position, FerryPos.transform.rotation, true).ToPlayer();
                player.lifestate = BaseCombatEntity.LifeState.Dead;
                player.ResetLifeStateOnSpawn = false;
                player.SetFlag(BaseEntity.Flags.Protected, true);
                player.Spawn();
                StartSleeping(player);
                player.CancelInvoke(player.KillMessage);
                player.userID = steamid;
                player.UserIDString = settings.steamid;
                player.displayName = settings.name;
                player.eyeHistory.Clear();
                player.lastTickTime = 0f;
                player.lastInputTime = 0f;
                player.stats.Init();
                player.InitializeHealth(settings.health, settings.maxhealth);
                player.metabolism.hydration.SetValue(settings.hydration);
                player.metabolism.calories.SetValue(settings.calories);
                DebugEx.Log(string.Format("{0} with steamid {1} joined from OpenNexus", player.displayName, player.userID), StackTraceLogType.None);
                player.SetParent(FerryPos);
                player.transform.localPosition = settings.pos;
                player.transform.localRotation = settings.rot;
                player.TransformChanged();
                setblueprints(player, settings.blueprints);
                setmodifiers(player, settings.mods);
                setteams(player, settings.team);
                player.SendNetworkUpdateImmediate();
                JoinedViaNexus.Add(player);
                if (config._ServerSettings.ShowDebugMsg) Puts("ProcessBasePlayer Setting ready flag for player to transition");
                plugin.UpdatePlayers(plugin.thisserverip + ":" + plugin.thisserverport, plugin.thisserverip + ":" + plugin.thisserverport, "Ready", settings.steamid.ToString());
                return new Dictionary<string, BaseNetworkable>() { { settings.steamid, player } };
            }
            return null;
        }

        private void GiveContents(string ownerid, int id, float condition, int amount, ulong skinid, int code, string imgdata, string oggdata, string text, int slot, int container, string mods, Dictionary<string, BaseNetworkable> CreatedEntitys)
        {
            if (CreatedEntitys.ContainsKey(ownerid))
            {
                //Find what the item belongs to
                var FoundEntity = CreatedEntitys[ownerid];
                if (FoundEntity != null)
                {
                    //Add weapon mods
                    List<string> Wmods = new List<string>();
                    if (mods != "")
                    {
                        string[] wmod = mods.Split(new string[] { "<ML>" }, System.StringSplitOptions.RemoveEmptyEntries);
                        foreach (string w in wmod) { Wmods.Add(w); }
                    }

                    //Ridable horse items
                    RidableHorse rh = FoundEntity as RidableHorse;
                    if (rh != null)
                    {
                        //put into horse inventory
                        Item item = BuildItem(id, amount, skinid, condition, code, imgdata, oggdata, text, Wmods);
                        if (item == null) return;
                        item.position = slot;
                        rh.inventory.Insert(item);
                        return;
                    }
                    //BasePlayers items
                    BasePlayer bp = FoundEntity as BasePlayer;
                    if (bp != null)
                    {
                        Item item;
                        switch (container)
                        {
                            //Put back in players inventory
                            case 0:
                                item = BuildItem(id, amount, skinid, condition, code, imgdata, oggdata, text, Wmods);
                                if (item != null)
                                {
                                    if (!item.MoveToContainer(bp.inventory.containerWear, slot, true, true))
                                    {
                                        item.position = slot;
                                        bp.inventory.containerWear.Insert(item);
                                    }
                                }
                                break;
                            case 1:
                                item = BuildItem(id, amount, skinid, condition, code, imgdata, oggdata, text, Wmods);
                                if (item != null)
                                {
                                    if (!item.MoveToContainer(bp.inventory.containerBelt, slot, true, true))
                                    {
                                        item.position = slot;
                                        bp.inventory.containerBelt.Insert(item);
                                    }
                                }
                                break;
                            case 2:
                                item = BuildItem(id, amount, skinid, condition, code, imgdata, oggdata, text, Wmods);
                                if (item != null)
                                {
                                    if (!item.MoveToContainer(bp.inventory.containerMain, slot, true, true))
                                    {
                                        item.position = slot;
                                        bp.inventory.containerMain.Insert(item);
                                    }
                                }
                                break;
                        }
                        return;
                    }
                }
            }
        }

        private Item BuildItem(int id, int amount, ulong skin, float condition, int code, string imgdata, string oggdata, string text, List<string> mods)
        {
            Item item = CreateItem(id, amount, skin, condition, code, imgdata, oggdata, text);
            if (item == null) { return null; }
            //Setup guns so they work when given to player
            BaseProjectile weapon = item.GetHeldEntity() as BaseProjectile;
            if (weapon != null) { weapon.primaryMagazine.contents = weapon.primaryMagazine.capacity; }
            //Set up mods on gun
            if (mods != null && mods.Count > 0)
            {
                foreach (string mod in mods)
                {
                    if (mod.Contains("<M>"))
                    {
                        //Apply mods if its a gun
                        string[] modsetting = mod.Split(new string[] { "<M>" }, System.StringSplitOptions.RemoveEmptyEntries);
                        string ttext = "";
                        if (modsetting.Length == 4){ttext = modsetting[3];}
                        if (modsetting.Length < 3) { continue; }
                        int _int;
                        int _int2;
                        float _float;
                        if (int.TryParse(modsetting[0], out _int) && float.TryParse(modsetting[1], out _float) && int.TryParse(modsetting[2], out _int2))
                        {
                            Item moditem = CreateItem(_int, _int2, 0, _float, code, imgdata, oggdata, ttext, 0);
                            if (moditem != null){if (!moditem.MoveToContainer(item.contents)) { item.contents.Insert(moditem); }}
                        }
                    }
                }
            }
            return item;
        }

        private Item CreateItem(int id, int amount, ulong skinid, float condition, int code, string imgdata, string oggdata, string text, int blueprintTarget = 0)
        {
            //Create a item
            Item item = ItemManager.Create(ItemManager.FindItemDefinition(id), amount, skinid);
            if (blueprintTarget != 0) { item.blueprintTarget = blueprintTarget; }
            //Reapply ImageData to photos and oggdata to cassettes
            if (item.instanceData != null)
            {
                item.instanceData.dataInt = code;
                BaseNetworkable baseNetworkable = BaseNetworkable.serverEntities.Find(item.instanceData.subEntity);
                if (baseNetworkable != null)
                {
                    PhotoEntity photoEntity;
                    if ((photoEntity = (baseNetworkable as PhotoEntity)) != null)
                    {
                        if (imgdata.Contains("<IDATA>"))
                        {
                            string[] ImageStrings = imgdata.Split(new string[] { "<IDATA>" }, System.StringSplitOptions.RemoveEmptyEntries);
                            if (ImageStrings.Length == 2)
                            {
                                ulong _ulong;
                                if (ulong.TryParse(ImageStrings[0], out _ulong)) { photoEntity.SetImageData(_ulong, Convert.FromBase64String(ImageStrings[1])); }
                            }
                        }
                    }
                    Cassette cassetteEntity;
                    if ((cassetteEntity = (baseNetworkable as Cassette)) != null)
                    {
                        if (oggdata.Contains("<SDATA>"))
                        {
                            string[] SoundStrings = oggdata.Split(new string[] { "<SDATA>" }, System.StringSplitOptions.RemoveEmptyEntries);
                            if (SoundStrings.Length == 2)
                            {
                                ulong _ulong;
                                if (ulong.TryParse(SoundStrings[0], out _ulong))
                                {
                                    cassetteEntity.ClearSavedAudio();
                                    cassetteEntity.CreatorSteamId = _ulong;
                                    cassetteEntity.SetAudioId(FileStorage.server.Store(Convert.FromBase64String(SoundStrings[1]), global::FileStorage.Type.ogg, cassetteEntity.net.ID, 0U), _ulong);
                                }
                            }
                        }
                    }
                }
            }
            //Add keycode back to keys
            if (item.info.shortname.Contains(".key"))
            {
                ProtoBuf.Item.InstanceData instanceData = Facepunch.Pool.Get<ProtoBuf.Item.InstanceData>();
                instanceData.dataInt = code;
                item.instanceData = instanceData;
            }
            item.condition = condition;
            if (text != "") { item.text = text; }       
            item.MarkDirty();
            return item;
        }
 
        private string CreatePacket(List<BaseNetworkable> Transfere, BaseEntity FerryPos, string dat = "")
        {
            if (FerryPos == null || Transfere == null) { return ""; }
            //Create packet to send to hub
            var data = new Dictionary<string, List<Dictionary<string, string>>>();
            var itemlist = new List<Dictionary<string, string>>();
            //Loop though all basenetworkables that are parented to ferry
            foreach (BaseEntity entity in Transfere)
            {
                if (entity == null) { continue; }
                //Create a baseplayer packet
                BasePlayer baseplayer = entity as BasePlayer;
                if (baseplayer != null)
                {
                    itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(basePlayer(baseplayer, FerryPos));
                    data.Add("BasePlayer[" + baseplayer.UserIDString + "]", itemlist);
                    itemlist = new List<Dictionary<string, string>>();
                    //Label items with wich container they came from
                    if (baseplayer.inventory.containerWear.itemList != null)
                    {
                        foreach (Item item in baseplayer.inventory.containerWear.itemList.ToArray())
                        {
                            if (item != null)
                            {
                                itemlist.Add(Contents(item, baseplayer.UserIDString, item.position.ToString(), "0"));
                                item.Remove();
                            }
                        }
                    }
                    if (baseplayer.inventory.containerBelt.itemList != null)
                    {
                        foreach (Item item in baseplayer.inventory.containerBelt.itemList.ToArray())
                        {
                            if (item != null)
                            {
                                itemlist.Add(Contents(item, baseplayer.UserIDString, item.position.ToString(), "1"));
                                item.Remove();
                            }
                        }
                    }
                    if (baseplayer.inventory.containerMain.itemList != null)
                    {
                        foreach (Item item in baseplayer.inventory.containerMain.itemList.ToArray())
                        {
                            if (item != null)
                            {
                                itemlist.Add(Contents(item, baseplayer.UserIDString, item.position.ToString(), "2"));
                                item.Remove();
                            }
                        }
                    }
                    //Attach inventory and plugin data to packet
                    if (itemlist.Count != 0) { data.Add("BasePlayerInventory[" + baseplayer.UserIDString + "]", itemlist); }
                    if (Backpacks != null && config._PluginSettings.BackPacks) { data.Add("BasePlayerBackpackData[" + baseplayer.UserIDString + "]", GetBackpackData(baseplayer.UserIDString)); }
                    if (Economics != null && config._PluginSettings.Economics) { data.Add("BasePlayerEconomicsData[" + baseplayer.UserIDString + "]", GetEconomicsData(baseplayer.UserIDString)); }
                    if (ZLevelsRemastered != null && config._PluginSettings.ZLevelsRemastered) { data.Add("BasePlayerZLevelsRemasteredData[" + baseplayer.UserIDString + "]", GetZLevelsRemasteredData(baseplayer.UserIDString)); }
                    if (ServerRewards && config._PluginSettings.ServerRewards) { data.Add("BasePlayerServerRewardsData[" + baseplayer.UserIDString + "]", GetServerRewardsData(baseplayer.UserIDString)); }
                    //Show Open Nexus Screen
                    if (FerryPos is BasePlayer) { }//Is admin command so dont do transfere screen.
                    else
                    {
                        plugin.AdjustConnectionScreen(baseplayer, "Open Nexus Transfering Data", 10);
                        baseplayer.ClientRPCPlayer(null, baseplayer, "StartLoading");
                        baseplayer.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, false);
                    }
                    continue;
                }
                //create modular car packet
                ModularCar car = entity as ModularCar;
                if (car != null)
                {
                    modularCar(car, FerryPos, ref data);
                    continue;
                }
                //Create a horse packet
                RidableHorse horse = entity as RidableHorse;
                if (horse != null)
                {
                    itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(basehorse(horse, FerryPos));
                    data.Add("BaseHorse[" + horse.net.ID.ToString() + "]", itemlist);
                    itemlist = new List<Dictionary<string, string>>();
                    if (horse.inventory.itemList != null)
                    {
                        foreach (Item item in horse.inventory.itemList.ToArray())
                        {
                            if (item != null)
                            {
                                itemlist.Add(Contents(item, horse.net.ID.ToString(), item.position.ToString()));
                                item.Remove();
                            }
                        }
                    }
                    data.Add("BaseHorseInventory[" + horse.net.ID.ToString() + "]", itemlist);
                    continue;
                }
                //create helicopter packet
                MiniCopter helicopter = entity as MiniCopter;
                if (helicopter != null)
                {
                    itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(baseVechicle(helicopter, FerryPos));
                    data.Add("MiniCopter[" + helicopter.net.ID + "]", itemlist);
                    continue;
                }
                //create boat packet
                BaseBoat boat = entity as BaseBoat;
                if (boat != null)
                {
                    itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(baseVechicle(boat, FerryPos));
                    data.Add("BaseBoat[" + boat.net.ID + "]", itemlist);
                    continue;
                }
                //create magnet crane packet
                BaseCrane crane = entity as BaseCrane;
                if (crane != null)
                {
                    itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(baseVechicle(crane, FerryPos));
                    data.Add("BaseCrane[" + crane.net.ID + "]", itemlist);
                    continue;
                }
                //create snowmobile packet
                Snowmobile sm = entity as Snowmobile;
                if (sm != null)
                {
                    itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(baseVechicle(sm, FerryPos));
                    data.Add("SnowMobile[" + sm.net.ID + "]", itemlist);
                    continue;
                }
                //create magnet sub packet
                BaseSubmarine sub = entity as BaseSubmarine;
                if (sub != null)
                {
                    itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(baseVechicle(sub, FerryPos));
                    data.Add("BaseSubmarine[" + sub.net.ID + "]", itemlist);
                    continue;
                }
            }
            //Seralize and tag as OpenNexus packet
            dat += "<OpenNexus>" + Newtonsoft.Json.JsonConvert.SerializeObject(data);
            return dat;
        }

        private List<Dictionary<string, string>> GetBackpackData(string Owner)
        {
            List<Dictionary<string, string>> Data = new List<Dictionary<string, string>>();
            ItemContainer Backpack;
            //Gets data with API
            Backpack = Backpacks?.Call<ItemContainer>("API_GetBackpackContainer", ulong.Parse(Owner));
            //If has data
            if (Backpack != null)
            {
                //Create list of items in backpack
                foreach (Item item in Backpack.itemList.ToArray())
                {
                    if (item != null)
                    {
                        Data.Add(Contents(item, Owner, item.position.ToString()));
                    }
                }
            }
            return Data;
        }

        private void SetBackpacksData(string key, List<Dictionary<string, string>> packets)
        {
            if (Backpacks != null)
            {
                ItemContainer Backpack;
                //Gets data with API
                string owneridparsed = key.Replace("]", "").Replace("BasePlayerBackpackData[", "");
                Backpack = Backpacks?.Call<ItemContainer>("API_GetBackpackContainer", ulong.Parse(owneridparsed));
                if (Backpack != null)
                {
                    BaseSettings settings = new BaseSettings();
                    settings.ProcessPacket(packets, null, 2, Backpack);
                }
            }
        }

        //Reads players server rewards balance
        private List<Dictionary<string, string>> GetServerRewardsData(string Owner)
        {
            ulong _ulong;
            if (!ulong.TryParse(Owner, out _ulong)) { return null; }
            List<Dictionary<string, string>> Data = new List<Dictionary<string, string>>();
            object rwpoints = ServerRewards?.Call<object>("CheckPoints", _ulong);
            if (rwpoints != null)
            {
                Data.Add(new Dictionary<string, string> { { Owner, rwpoints.ToString() } });
                ServerRewards?.Call<object>("TakePoints", _ulong, int.Parse(rwpoints.ToString()));
            }
            return Data;
        }

        //Sets players server rewards balance
        private void SetServerRewardsData(string Owner, string Balance)
        {
            if (ServerRewards != null && Owner != null && Balance != null)
            {
                ServerRewards?.Call<string>("AddPoints", Owner, int.Parse(Balance));
            }
        }

        //Reads players Economics balance
        private List<Dictionary<string, string>> GetEconomicsData(string Owner)
        {
            List<Dictionary<string, string>> Data = new List<Dictionary<string, string>>();
            Data.Add(new Dictionary<string, string> { { Owner, Economics?.Call<string>("Balance", Owner) } });
            return Data;
        }

        //Sets players Economics balance
        private void SetEconomicsData(string Owner, string Balance) { if (Economics != null && Owner != null && Balance != null) { Economics?.Call<string>("SetBalance", Owner, double.Parse(Balance)); } }

        //Get Zlevel data
        private List<Dictionary<string, string>> GetZLevelsRemasteredData(string Owner)
        {
            List<Dictionary<string, string>> Data = new List<Dictionary<string, string>>();
            string pinfo = ZLevelsRemastered?.Call<string>("api_GetPlayerInfo", ulong.Parse(Owner));
            if (pinfo != null || pinfo != "") { Data.Add(new Dictionary<string, string> { { Owner, pinfo } }); }
            return Data;
        }

        //Sets Zlevel data
        private void SetZLevelsRemasteredData(string ownerid, string packet) { if (ZLevelsRemastered != null) { ZLevelsRemastered.Call<bool>("api_SetPlayerInfo", ulong.Parse(ownerid), packet); } }

        private void GetData(Item item, ref string code,ref string imgdata,ref string oggdata)
        {
            if(item == null) { return; }
            ProtoBuf.Item.InstanceData idat = item.instanceData;
            if (idat != null)
            {
                //Add keys code data
                if (idat.dataInt != 0) { code = idat.dataInt.ToString(); }
                //Add image / sound data
                if (item.instanceData.subEntity != 0)
                {
                    BaseNetworkable baseNetworkable = BaseNetworkable.serverEntities.Find(item.instanceData.subEntity);
                    if (baseNetworkable != null)
                    {
                        PhotoEntity photoEntity;
                        if ((photoEntity = (baseNetworkable as PhotoEntity)) != null)
                        {
                            imgdata = photoEntity.PhotographerSteamId.ToString() + "<IDATA>" + Convert.ToBase64String(FileStorage.server.Get(photoEntity.ImageCrc, FileStorage.Type.jpg, photoEntity.net.ID));
                        }
                        Cassette cassetteEntity;
                        if ((cassetteEntity = (baseNetworkable as Cassette)) != null)
                        {
                            oggdata = cassetteEntity.CreatorSteamId.ToString() + "<SDATA>" + Convert.ToBase64String(FileStorage.server.Get(cassetteEntity.AudioId, FileStorage.Type.ogg, cassetteEntity.net.ID));
                        }
                    }
                }
            }
        }


        private Dictionary<string, string> Contents(Item item, string Owner, string slot = "0", string container = "0")
        {
            //Item packet
            string code = "0";
            string mods = "";
            string imgdata = "";
            string oggdata = "";
            GetData(item, ref code, ref imgdata, ref oggdata);
            //Create list of mods on weapon
            if (item.contents != null)
            {
                foreach (Item mod in item.contents.itemList)
                {
                    if (mod.info.itemid != 0)
                    {
                        GetData(mod, ref code, ref imgdata, ref oggdata);
                        mods += (mod.info.itemid.ToString() + "<M>" + mod.condition.ToString() + "<M>" + mod.amount.ToString() + "<M>" + mod.text + "<ML>");
                    }
                }
            }
            var itemdata = new Dictionary<string, string>
                        {
                        { "ownerid", Owner },
                        { "id", item.info.itemid.ToString() },
                        { "condition", item.condition.ToString() },
                        { "amount", item.amount.ToString() },
                        { "skinid", item.skin.ToString() },
                        { "code", code},
                        { "text", item.text},
                        { "imgdata", imgdata},
                        { "oggdata", oggdata},
                        { "mods", mods},
                        { "container", container },
                        { "slot", slot }
                        };
            return itemdata;
        }

        private List<Dictionary<string, string>> ModularItems(ItemContainer storageInventory, string thiscarID, string socket)
        {
            List<Dictionary<string, string>> itemlist = new List<Dictionary<string, string>>();
            if (storageInventory != null)
            {
                foreach (Item item in storageInventory.itemList.ToArray())
                {
                    if (item != null)
                    {
                        itemlist.Add(Contents(item, thiscarID, item.position.ToString(), socket));
                        item.Remove();
                    }
                }
            }
            return itemlist;
        }

        private void modularCar(ModularCar car, BaseEntity FerryPos, ref Dictionary<string, List<Dictionary<string, string>>> data)
        {
            if (car == null || FerryPos == null) return;
            string thiscarID = car.net.ID.ToString();
            List<Dictionary<string, string>> itemlist = new List<Dictionary<string, string>>();
            itemlist.Add(baseVechicle(car, FerryPos, car.TotalSockets.ToString(), car.carLock.LockID.ToString()));
            data.Add("ModularCar[" + thiscarID + "]", itemlist);
            int socket = 0;
            foreach (BaseVehicleModule moduleEntity in car.AttachedModuleEntities)
            {
                VehicleModuleEngine vehicleModuleEngine = moduleEntity as VehicleModuleEngine;
                //Create packet of engine parts
                if (vehicleModuleEngine != null)
                {
                    data.Add("ModularCarItems[" + vehicleModuleEngine.net.ID + "]", ModularItems(vehicleModuleEngine.GetContainer().inventory, thiscarID, socket.ToString()));
                    socket += moduleEntity.GetNumSocketsTaken();
                    continue;
                }
                VehicleModuleStorage vehicleModuleStorage = moduleEntity as VehicleModuleStorage;
                if (vehicleModuleStorage != null)
                {
                    data.Add("ModularCarItems[" + vehicleModuleStorage.net.ID + "]", ModularItems(vehicleModuleStorage.GetContainer().inventory, thiscarID, socket.ToString()));
                    socket += moduleEntity.GetNumSocketsTaken();
                    continue;
                }
                socket += moduleEntity.GetNumSocketsTaken();
                VehicleModuleCamper vehicleModuleCamper = moduleEntity as VehicleModuleCamper;
                if (vehicleModuleCamper != null)
                {
                    //create packet of camper module parts
                    itemlist = new List<Dictionary<string, string>>();
                    //Store position of stoage container
                    ItemContainer storage = vehicleModuleCamper.activeStorage.Get(true).inventory;
                    if (storage != null)
                    {
                        foreach (Item item in storage.itemList.ToList())
                        {
                            if (item != null)
                            {
                                itemlist.Add(Contents(item, thiscarID, item.position.ToString(), "0"));
                                item.Remove();
                            }
                        }
                    }
                    ItemContainer locker = vehicleModuleCamper.activeLocker.Get(true).inventory;
                    if (locker != null)
                    {
                        foreach (Item item in locker.itemList.ToList())
                        {
                            if (item != null)
                            {
                                itemlist.Add(Contents(item, thiscarID, item.position.ToString(), "1"));
                                item.Remove();
                            }
                        }
                    }
                    ItemContainer bbq = vehicleModuleCamper.activeBbq.Get(true).inventory;
                    if (bbq != null)
                    {
                        foreach (Item item in bbq.itemList.ToList())
                        {
                            if (item != null)
                            {
                                itemlist.Add(Contents(item, thiscarID, item.position.ToString(), "2"));
                                item.Remove();
                            }
                        }
                    }
                    if (itemlist.Count != 0) { data.Add("ModularCarCamper[" + vehicleModuleCamper.net.ID.ToString() + "]", itemlist); }
                    socket++;
                    continue;
                }
            }
        }

        private Dictionary<string, string> baseVechicle(BaseVehicle bv, BaseEntity FerryPos, string socket = "0", string lockid = "0")
        {
            //baseVechicle packet
            if (bv == null || FerryPos == null) { return null; }
            string Mounts = "";
            string Modules = "";
            string Conditions = "";
            string Childred = "";
            string bags = "";
            //Check if has unlimited fuel mods
            string unlimitedfuel = "False";
            MiniCopter mc = bv as MiniCopter;
            if (mc != null && mc.fuelPerSec == 0) { unlimitedfuel = "True"; }
            MotorRowboat boat = bv as MotorRowboat;
            if (boat != null && boat.fuelPerSec == 0) { unlimitedfuel = "True"; }
            BaseCrane crane = bv as BaseCrane;
            if (crane != null && crane.fuelPerSec == 0) { unlimitedfuel = "True"; }
            BaseSubmarine sub = bv as BaseSubmarine;
            if (sub != null && sub.maxFuelPerSec == 0) { unlimitedfuel = "True"; }
            Snowmobile sm = bv as Snowmobile;
            if (sm != null && sm.maxFuelPerSec == 0) { unlimitedfuel = "True"; }
            //Create module and there condition list if car
            ModularCar car = bv as ModularCar;
            if (car != null)
            {
                if (car.AttachedModuleEntities != null && car.AttachedModuleEntities.Count > 0)
                {
                    foreach (var moduleEntity in car.AttachedModuleEntities)
                    {
                        //If its engine module check for unlimited fuel mod
                        VehicleModuleEngine vme = moduleEntity as VehicleModuleEngine;
                        if (vme != null) { if ((moduleEntity as VehicleModuleEngine).engine.maxFuelPerSec == 0) { unlimitedfuel = "True"; } }
                        //Create module info
                        try { Modules += moduleEntity.AssociatedItemInstance.info.itemid + "|"; } catch { Modules += "null|"; }
                        try { Conditions += moduleEntity._health + "|"; } catch { Modules += "null|"; }
                        foreach (BaseEntity baseEntity in car.children)
                        {
                            if (baseEntity == null) continue;
                            foreach (BaseEntity baseEntity2 in baseEntity.children)
                            {
                                if (baseEntity2 == null) continue;
                                SleepingBag sleepingBagCamper;
                                if ((sleepingBagCamper = (baseEntity2 as SleepingBag)) != null) { bags += sleepingBagCamper.niceName + "<uid>" + sleepingBagCamper.deployerUserID.ToString() + "<bag>"; }
                            }
                        }
                    }
                }
            }
            //Dont do this for modular cars or it breaks them
            else
            {
                //Get all seat points
                if (bv.mountPoints != null && bv.mountPoints.Count > 0){foreach (BaseVehicle.MountPointInfo m in bv.mountPoints) { Mounts += "<mount>" + m.pos.ToString() + "&" + m.rot.ToString() + "&" + m.mountable.transform.localRotation.ToString(); }}
                Childred = GetAllFamily(bv) + Mounts;
            }
            var itemdata = new Dictionary<string, string>
                        {
                        { "ownerid", bv.OwnerID.ToString() },
                        { "prefab", bv.PrefabName },
                        { "position", FerryPos.transform.InverseTransformPoint(bv.transform.position).ToString() },
                        { "rotation", (bv.transform.localRotation).ToString()},
                        { "health", bv._health.ToString() },
                        { "maxhealth", bv.MaxHealth().ToString() },
                        { "fuel", bv.GetFuelSystem().GetFuelAmount().ToString()},
                        { "lockid", lockid },
                        { "modules", Modules},
                        { "children", Childred },
                        { "conditions", Conditions},
                        { "unlimitedfuel", unlimitedfuel},
                        { "bags", bags },
                        { "netid", bv.net.ID.ToString()},
                        { "sockets", socket },
                        };
            return itemdata;
        }

        private Dictionary<string, string> basePlayer(BasePlayer baseplayer, BaseEntity FerryPos)
        {
            //baseplayer packet
            int seat = 0;
            BaseVehicle bv = baseplayer.GetMountedVehicle();
            //Get seat info
            if (bv != null) { seat = bv.GetPlayerSeat(baseplayer); }
            var itemdata = new Dictionary<string, string>
                        {
                        { "steamid", baseplayer.UserIDString },
                        { "name", baseplayer.displayName.ToString() },
                        { "position", FerryPos.transform.InverseTransformPoint(baseplayer.transform.position).ToString() },
                        { "rotation", (baseplayer.transform.localRotation).ToString()},
                        { "health", baseplayer._health.ToString() },
                        { "maxhealth", baseplayer._maxHealth.ToString() },
                        { "hydration", baseplayer.metabolism.hydration.value.ToString() },
                        { "calories", baseplayer.metabolism.calories.value.ToString() },
                        { "blueprints", getblueprints(baseplayer) },
                        { "modifiers", getmodifiers(baseplayer) },
                        { "team", getteams(baseplayer) },
                        { "seat", seat.ToString() },
                        { "mounted", baseplayer.isMounted.ToString() }
                        };
            return itemdata;
        }

        private Dictionary<string, string> basehorse(RidableHorse horse, BaseEntity FerryPos)
        {
            //basehorse packet
            var itemdata = new Dictionary<string, string>
                        {
                        { "currentBreed", horse.currentBreed.ToString() },
                        { "prefab", horse.PrefabName },
                        { "position", FerryPos.transform.InverseTransformPoint(horse.transform.position).ToString() },
                        { "rotation", (horse.transform.localRotation).ToString()},
                        { "maxSpeed", horse.maxSpeed.ToString()},
                        { "maxHealth", horse._maxHealth.ToString()},
                        { "health", horse._health.ToString() },
                        { "maxStaminaSeconds", horse.maxStaminaSeconds.ToString() },
                        { "staminaCoreSpeedBonus", horse.staminaCoreSpeedBonus.ToString() },
                        { "ownerid", horse.OwnerID.ToString() },
                        { "netid", horse.net.ID.ToString() },
                        { "flags", horse.HasFlag(BaseEntity.Flags.Reserved4).ToString()+" " +  horse.HasFlag(BaseEntity.Flags.Reserved5).ToString() + " " + horse.HasFlag(BaseEntity.Flags.Reserved6).ToString()}
                        };
            return itemdata;
        }

        private void AddChilditems(BaseEntity ent, string data)
        {
            //Get all items of a child entity
            IItemContainerEntity ice = ent as IItemContainerEntity;
            if (ice != null)
            {
                string[] itmlist = data.Split(new string[] { "<item>" }, System.StringSplitOptions.RemoveEmptyEntries);
                int id = 0;
                int amount = 0;
                ulong skin = 0;
                float condition = 0;
                int code = 0;
                string text = "";
                string imgdata = "";
                string oggdata = "";
                List<string> mods = new List<string>();
                foreach (string st in itmlist)
                {
                    string[] info = st.Split(',');
                    if (info.Length != 2) { continue; }
                    int _int;
                    float _float;
                    ulong _ulong;
                    switch (info[0])
                    {
                        case "id":
                            if (int.TryParse(info[1], out _int)) { id = _int; }
                            break;
                        case "condition":
                            if (float.TryParse(info[1], out _float)) { condition = _float; }
                            break;
                        case "amount":
                            if (int.TryParse(info[1], out _int)) { amount = _int; }
                            break;
                        case "skinid":
                            if (ulong.TryParse(info[1], out _ulong)) { skin = _ulong; }
                            break;
                        case "code":
                            if (int.TryParse(info[1], out _int)) { code = _int; }
                            break;
                        case "text":
                            text = info[1];
                            break;
                        case "imgdata":
                            imgdata = info[1];
                            break;
                        case "oggdata":
                            oggdata = info[1];
                            break;
                        case "mods":
                            if (info[1] != "") { string[] wmod = info[1].Split(new string[] { "<ML>" }, System.StringSplitOptions.RemoveEmptyEntries); foreach (string w in wmod) { mods.Add(w); } }
                            break;
                        case "slot":
                            if (int.TryParse(info[1], out _int))
                            {
                                Item i = BuildItem(id, amount, skin, condition, code, imgdata, oggdata, text, mods.ToList());
                                if (i == null) { continue; }
                                if (!i.MoveToContainer(ice.inventory, _int, true, true))
                                {
                                    i.position = _int;
                                    ice.inventory.Insert(i);
                                }
                            }
                            break;
                    }
                }
            }
        }

        private void AttachFamily(BaseEntity parent, string stringdata)
        {
            if(!config._PluginSettings.Parented) { return; }
            //Rebuild all child entitys
            if (stringdata == null || stringdata == "") { return; }
            Dictionary<uint, uint> RemapNetID = new Dictionary<uint, uint>();
            //Re add mount points
            string[] children = stringdata.Split(new string[] { "<mount>" }, System.StringSplitOptions.RemoveEmptyEntries);
            if (children.Length > 1)
            {
                BaseVehicle bv = parent as BaseVehicle;
                if (bv != null)
                {
                    List<string> StockPos = new List<string>();
                    foreach (BaseVehicle.MountPointInfo bmpi in bv.mountPoints) { StockPos.Add(bmpi.pos.ToString()); }
                    for (int i = 1; i < children.Length; i++)
                    {
                        string[] splitd = children[i].Split('&');
                        if (splitd.Length == 3)
                        {
                            if (StockPos.Contains(splitd[0])) { continue; }
                            BaseEntity baseEntity = GameManager.server.CreateEntity(bv.mountPoints[1].prefab.resourcePath);
                            BaseMountable baseMountable = baseEntity as BaseMountable;
                            if (baseMountable != null)
                            {
                                BaseVehicle.MountPointInfo bminfo = new BaseVehicle.MountPointInfo
                                {
                                    isDriver = false,
                                    pos = StringToVector3(splitd[0]),
                                    rot = StringToVector3(splitd[1]),
                                    bone = bv.mountPoints[1].bone,
                                    prefab = bv.mountPoints[1].prefab,
                                };

                                if (!baseMountable.enableSaving) { baseMountable.EnableSaving(true); }
                                if (bminfo.bone != "") { baseMountable.SetParent(bv, bminfo.bone, true, true); }
                                else { baseMountable.SetParent(bv, false, false); }
                                baseMountable.transform.localPosition = bminfo.pos;
                                baseMountable.transform.localRotation = StringToQuaternion(splitd[2]);
                                baseMountable.Spawn();
                                bminfo.mountable = baseMountable;
                                bv.mountPoints.Add(bminfo);
                            }
                        }
                    }
                    bv.SendNetworkUpdateImmediate();
                }
            }
            //Process Child entity
            string[] child;
            if (children.Length == 0) { child = stringdata.Split(new string[] { "<Child>" }, System.StringSplitOptions.RemoveEmptyEntries); }
            else { child = children[0].Split(new string[] { "<Child>" }, System.StringSplitOptions.RemoveEmptyEntries); }
            List<BaseEntity> Family = new List<BaseEntity>();
            foreach (BaseEntity be in parent.children)
            {
                //Stop getting stuk in reference loop
                if (Family.Contains(be)) { continue; }
                //Add to list
                Family.Add(be);
            }
            //Get orignal netid and get new netid
            foreach (string data in child)
            {
                if (data == null || data == "") { continue; }
                string[] cd = data.Split('|');
                if (cd[0].Contains("<Parent>"))
                {
                    //Update remap for netid change
                    RemapNetID.Add(uint.Parse(cd[0].Replace("<Parent>", "")), parent.net.ID);
                    parent.skinID = ulong.Parse(cd[5]);
                    SetFlags(parent, cd[8]);
                    continue;
                }
                Vector3 pos = StringToVector3(cd[2]);
                Quaternion rot = StringToQuaternion(cd[3]);
                Vector3 scale = StringToVector3(cd[4]);
                bool skip = false;
                foreach (BaseEntity b in Family)
                {
                    if (b.transform.localPosition == pos && b.transform.rotation == rot && b.transform.localScale == scale)
                    {
                        skip = true;
                        continue;
                    }
                }
                if (skip) { continue; }
                var e = GameManager.server.CreateEntity(StringPool.Get(uint.Parse(cd[1])), parent.transform.position + new Vector3(0, -10, 0));
                if (e == null) { continue; }
                uint oldnetid = uint.Parse(cd[0].Replace("<Child>", ""));
                if (cd[11] == "DestroyOnGroundMissing") { DestroyGroundComp(e); }
                if (cd[12] == "DestroyMeshCollider") { DestroyMeshCollider(e); }
                e.Spawn();
                if (!RemapNetID.ContainsKey(oldnetid)) { RemapNetID.Add(oldnetid, e.net.ID); }
                BaseNetworkable p = BaseNetworkable.serverEntities.Find(RemapNetID[uint.Parse(cd[15])]);
                e.SetParent(p as BaseEntity);
                e.transform.localPosition = pos;
                e.transform.localRotation = rot;
                e.transform.localScale = StringToVector3(cd[4]);
                e.skinID = ulong.Parse(cd[5]);
                SetFlags(e, cd[8]);
                StabilityEntity s = e as StabilityEntity;
                if (s != null && cd[9] != "null") { s.grounded = bool.Parse(cd[9]); }
                BaseCombatEntity c = e as BaseCombatEntity;
                if (c != null)
                {
                    c.InitializeHealth(float.Parse(cd[7]), float.Parse(cd[6]));
                    if (cd[10] != "null") { c.pickup.enabled = bool.Parse(cd[10]); }
                }
                if (cd[13] != "null") { AddLock(e, cd[13]); }
                if (cd[14] != "null") { AddChilditems(e, cd[14]); }
                e.SendNetworkUpdateImmediate();
            }
            parent.SendNetworkUpdateImmediate();
        }

        private void SetFlags(BaseEntity be, string data)
        {
            string[] f = data.Split(new string[] { "<BEF>" }, System.StringSplitOptions.RemoveEmptyEntries);
            List<bool> flags = new List<bool>();
            foreach (string s in f) { bool _bool; if (bool.TryParse(s, out _bool)) { flags.Add(_bool); } }
            be.SetFlag(BaseEntity.Flags.Broken, flags[0]);
            be.SetFlag(BaseEntity.Flags.Busy, flags[1]);
            be.SetFlag(BaseEntity.Flags.Debugging, flags[2]);
            be.SetFlag(BaseEntity.Flags.Disabled, flags[3]);
            be.SetFlag(BaseEntity.Flags.Locked, flags[4]);
            be.SetFlag(BaseEntity.Flags.On, flags[5]);
            be.SetFlag(BaseEntity.Flags.OnFire, flags[6]);
            be.SetFlag(BaseEntity.Flags.Open, flags[7]);
            //Set doors state
            if (be is Door) { NextTick(() => { (be as Door).SetOpen(be.flags.HasFlag(BaseEntity.Flags.Open)); }); }
            be.SetFlag(BaseEntity.Flags.Placeholder, flags[8]);
            be.SetFlag(BaseEntity.Flags.Protected, flags[9]);
            be.SetFlag(BaseEntity.Flags.Reserved1, flags[10]);
            be.SetFlag(BaseEntity.Flags.Reserved10, flags[11]);
            be.SetFlag(BaseEntity.Flags.Reserved11, flags[12]);
            be.SetFlag(BaseEntity.Flags.Reserved2, flags[13]);
            be.SetFlag(BaseEntity.Flags.Reserved3, flags[14]);
            be.SetFlag(BaseEntity.Flags.Reserved4, flags[15]);
            be.SetFlag(BaseEntity.Flags.Reserved5, flags[16]);
            be.SetFlag(BaseEntity.Flags.Reserved6, flags[17]);
            be.SetFlag(BaseEntity.Flags.Reserved7, flags[18]);
            be.SetFlag(BaseEntity.Flags.Reserved8, flags[19]);
            be.SetFlag(BaseEntity.Flags.Reserved9, flags[20]);
        }

        private string GetBaseEntity(BaseEntity be, bool parent = false)
        {
            string baseentity = "";
            //Use net id so can relink
            if (parent) { baseentity += "<Parent>" + be.net.ID + "|"; }
            else { baseentity += "<Child>" + be.net.ID + "|"; }
            baseentity += be.prefabID.ToString() + "|" + be.transform.localPosition.ToString() + "|" + be.transform.localRotation.ToString() + "|" + be.transform.localScale.ToString() + "|" + be.skinID.ToString() + "|" + be.MaxHealth().ToString() + "|" + be.Health().ToString() + "|";
            //Get all flags
            baseentity += "<BEF>" + be.HasFlag(BaseEntity.Flags.Broken) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Busy) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Debugging) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Disabled) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Locked) + "<BEF>" + be.HasFlag(BaseEntity.Flags.On) + "<BEF>" + be.HasFlag(BaseEntity.Flags.OnFire) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Open) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Placeholder) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Protected) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Reserved1) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Reserved10) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Reserved11) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Reserved2) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Reserved3) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Reserved4) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Reserved5) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Reserved6) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Reserved7) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Reserved8) + "<BEF>" + be.HasFlag(BaseEntity.Flags.Reserved9) + "|";
            //Check if ground
            StabilityEntity s = be as StabilityEntity;
            if (s == null) { baseentity += "null|"; }
            else { baseentity += s.grounded.ToString() + "|"; }
            //Check if can pickup
            BaseCombatEntity c = be as BaseCombatEntity;
            if (c == null) { baseentity += "null|"; }
            else { baseentity += c.pickup.enabled.ToString() + "|"; }
            //Disable ground check
            if (!be.GetComponent<DestroyOnGroundMissing>()) { baseentity += "DestroyOnGroundMissing|"; }
            else { baseentity += "null|"; }
            //Disable mesh collider
            if (!be.GetComponent<MeshCollider>()) { baseentity += "DestroyMeshCollider|"; }
            else { baseentity += "null|"; }
            //Codelock info
            if (be.GetSlot(0))
            {
                CodeLock codelock = be.GetSlot(0) as CodeLock;
                if (codelock != null)
                {
                    baseentity += "<CodeLock>" + codelock.transform.localPosition + "<CodeLock>" + codelock.transform.localRotation + "<CodeLock>" + codelock.code + "<CodeLock>" + codelock.HasFlag(CodeLock.Flags.Locked) + "<CodeLock>";
                    foreach (ulong id in codelock.whitelistPlayers) { baseentity += "<player>" + id; }
                    baseentity += "|";
                }
                else { baseentity += "null|"; }
            }
            else { baseentity += "null|"; }
            //Get container info
            IItemContainerEntity ic = be as IItemContainerEntity;
            if (ic == null) { baseentity += "null|"; }
            else
            {
                if (ic.inventory != null && ic.inventory.itemList != null)
                {
                    //int sl = 0;
                    List<Dictionary<string, string>> itmlist = new List<Dictionary<string, string>>();
                    foreach (Item item in ic.inventory.itemList.ToArray())
                    {
                        if (item == null) { continue; }
                        {
                            itmlist.Add(Contents(item, be.OwnerID.ToString(), item.position.ToString()));
                            item.Remove();
                        }
                    }
                    foreach (Dictionary<string, string> dict in itmlist) { foreach (KeyValuePair<string, string> itz in dict) { baseentity += itz.Key + "," + itz.Value + "<item>"; } }
                }
                baseentity += "|";
            }
            //Get parents netid so can relink
            BaseEntity p = be.GetParentEntity();
            if (p == null) { baseentity += "null"; }
            else { baseentity += be.GetParentEntity().net.ID.ToString(); }
            return baseentity;
        }

        private string GetAllFamily(BaseEntity parent)
        {
            if (!config._PluginSettings.Parented) { return ""; }
                List<BaseEntity> Family = new List<BaseEntity>();
            string XML = GetBaseEntity(parent, true);
            foreach (BaseEntity be in parent.children)
            {
                //Stop getting stuk in reference loop
                if (Family.Contains(be)) { continue; }
                //Add to list
                Family.Add(be);
                XML += GetBaseEntity(be);
                //Found children of chidren so run on them
                if (be.children != null || be.children.Count != 0) { GetAllFamily(be); }
            }
            return XML;
        }
        #endregion

        #region Classes
        public class BaseSettings
        {
            //Class of common used settings in deserilising
            public Quaternion rot = new Quaternion();
            public Vector3 pos = Vector3.zero;
            public int id;
            public string prefab = "";
            public float health = 0;
            public float maxhealth = 0;
            public int fuel = 0;
            public int lockid = 0;
            public ulong ownerid = 0;
            public string modules = "";
            public string children = "";
            public string conditions = "";
            public int slot = 0;
            public int amount = 0;
            public int container = 0;
            public string netid = "";
            public int currentBreed = 0;
            public float maxStaminaSeconds = 0;
            public float staminaCoreSpeedBonus = 0;
            public string[] flags = new string[20];
            public float maxSpeed = 0;
            public string steamid = "";
            public string name = "";
            public float condition = 0;
            public ulong skinid = 0;
            public int code = 0;
            public string text = "";
            public string imgdata = "";
            public string oggdata = "";
            public float hydration = 0;
            public string team = "";
            public float calories = 0;
            public bool mounted = false;
            public int seat = 0;
            public bool unlimitedfuel = false;
            public string[] blueprints = new string[0];
            public string[] mods = new string[0];
            public string mod = "";
            public string bags = "";

            public void ProcessPacket(List<Dictionary<string, string>> packets, Dictionary<string, ModularCar> SpawnedCars = null, int containertype = -1, ItemContainer sc = null, Dictionary<string, BaseNetworkable> CreatedEntitys = null)
            {
                foreach (Dictionary<string, string> i in packets)
                {
                    foreach (KeyValuePair<string, string> ii in i)
                    {
                        int _int;
                        float _float;
                        ulong _ulong;
                        bool _bool;
                        switch (ii.Key)
                        {
                            case "rotation":
                                rot = plugin.StringToQuaternion(ii.Value);
                                break;
                            case "position":
                                pos = plugin.StringToVector3(ii.Value);
                                break;
                            case "id":
                                if (int.TryParse(ii.Value, out _int)) { id = _int; }
                                break;
                            case "amount":
                                if (int.TryParse(ii.Value, out _int)) { amount = _int; }
                                break;
                            case "prefab":
                                prefab = ii.Value;
                                break;
                            case "health":
                                if (float.TryParse(ii.Value, out _float)) { health = _float; }
                                break;
                            case "maxhealth":
                                if (float.TryParse(ii.Value, out _float)) { maxhealth = _float; }
                                break;
                            case "fuel":
                                if (int.TryParse(ii.Value, out _int)) { fuel = _int; }
                                break;
                            case "lockid":
                                if (int.TryParse(ii.Value, out _int)) { lockid = _int; }
                                break;
                            case "ownerid":
                                if (ulong.TryParse(ii.Value, out _ulong)) { ownerid = _ulong; }
                                break;
                            case "container":
                                if (int.TryParse(ii.Value, out _int)) { container = _int; }
                                break;
                            case "slot":
                                if (int.TryParse(ii.Value, out _int)) { slot = _int; }
                                switch (containertype)
                                {
                                    case 0:
                                        plugin.ProcessCamper(SpawnedCars, this);
                                        break;
                                    case 1:
                                        plugin.ProcessModuleParts(SpawnedCars, this);
                                        break;
                                    case 2: //Backpack
                                        string[] wmod = new string[0];
                                        if (mod != "")
                                        {
                                            wmod = mod.Split(new string[] { "<ML>" }, System.StringSplitOptions.RemoveEmptyEntries);
                                        }
                                        Item item = plugin.BuildItem(id, amount, skinid, condition, code, imgdata, oggdata, text, wmod.ToList());
                                        if (item != null)
                                        {
                                            if (!item.MoveToContainer(sc, slot, true, true))
                                            {
                                                item.position = _int;
                                                sc.Insert(item);
                                            }
                                        }
                                        break;
                                    case 3: //Invontorys
                                        plugin.GiveContents(ownerid.ToString(), id, condition, amount, skinid, code, imgdata, oggdata, text, slot, container, mod, CreatedEntitys);
                                        break;
                                }
                                break;
                            case "condition":
                                if (float.TryParse(ii.Value, out _float)) { condition = _float; }
                                break;
                            case "skinid":
                                if (ulong.TryParse(ii.Value, out _ulong)) { skinid = _ulong; }
                                break;
                            case "code":
                                if (int.TryParse(ii.Value, out _int)) { code = _int; }
                                break;
                            case "text":
                                text = ii.Value;
                                break;
                            case "imgdata":
                                imgdata = ii.Value;
                                break;
                            case "oggdata":
                                oggdata = ii.Value;
                                break;
                            case "modules":
                                modules = ii.Value;
                                break;
                            case "unlimitedfuel":
                                if (bool.TryParse(ii.Value, out _bool)) { unlimitedfuel = _bool; }
                                break;
                            case "children":
                                children = ii.Value;
                                break;
                            case "conditions":
                                conditions = ii.Value;
                                break;
                            case "netid":
                                netid = ii.Value;
                                break;
                            case "maxSpeed":
                                if (float.TryParse(ii.Value, out _float)) { maxSpeed = _float; }
                                break;
                            case "maxStaminaSeconds":
                                if (float.TryParse(ii.Value, out _float)) { maxStaminaSeconds = _float; }
                                break;
                            case "staminaCoreSpeedBonus":
                                if (float.TryParse(ii.Value, out _float)) { staminaCoreSpeedBonus = _float; }
                                break;
                            case "flags":
                                flags = ii.Value.Split(' ');
                                break;
                            case "steamid":
                                steamid = ii.Value;
                                break;
                            case "name":
                                name = ii.Value;
                                break;
                            case "hydration":
                                if (float.TryParse(ii.Value, out _float)) { hydration = _float; }
                                break;
                            case "calories":
                                if (float.TryParse(ii.Value, out _float)) { calories = _float; }
                                break;
                            case "blueprints":
                                blueprints = ii.Value.Split(new string[] { "<BP>" }, System.StringSplitOptions.RemoveEmptyEntries);
                                break;
                            case "team":
                                team = ii.Value;
                                break;
                            case "modifiers":
                                mods = ii.Value.Split(new string[] { "<MF>" }, System.StringSplitOptions.RemoveEmptyEntries);
                                break;
                            case "mods":
                                mod = ii.Value;
                                break;
                            case "seat":
                                if (int.TryParse(ii.Value, out _int)) { seat = _int; }
                                break;
                            case "mounted":
                                mounted = (ii.Value.ToLower().Contains("true"));
                                ulong usteamid;
                                if (ulong.TryParse(steamid, out usteamid)) { if (mounted && !plugin.SeatPlayers.ContainsKey(usteamid)) { plugin.SeatPlayers.Add(usteamid, seat); } }
                                break;
                            case "bags":
                                bags = ii.Value;
                                break;
                        }
                    }
                }
            }
        }
        private class ModuleData { public List<float> condition = new List<float>(); public List<ItemModVehicleModule> modules = new List<ItemModVehicleModule>(); }
        //Open Nexus Ferry Code
        public class OpenNexusIsland : BaseEntity { public GameObjectRef MapMarkerPrefab; public Transform MapMarkerLocation; }
        public class IslandData { public BaseEntity Island; public Vector3 location; public Quaternion rotation; }
        //Open Nexus Ferry Code
        public class OpenNexusFerry : BaseEntity
        {
            //Variables
            Transform FerryPos = null;
            public string ServerIP = "";
            public string ServerPort = "";
            public int retrys = 0;
            public bool ServerSynced = false;
            private bool LoadingSync = false;
            private OpenNexusFerry.State _state = OpenNexusFerry.State.Stopping;
            public Transform Docked = new GameObject().transform;
            public Transform Departure = new GameObject().transform;
            public Transform Arrival = new GameObject().transform;
            public Transform Docking = new GameObject().transform;
            public Transform CastingOff = new GameObject().transform;
            public Transform EjectionZone = new GameObject().transform;
            private TimeSince _sinceStartedWaiting;
            private bool _isTransferring = false;
            public List<BaseNetworkable> DockedEntitys = new List<BaseNetworkable>();

            //Avaliable States
            private enum State
            {
                Invalid = 0,
                Arrival = 1,
                Docking = 2,
                Stopping = 3,
                Waiting = 4,
                CastingOff = 5,
                Departure = 6,
                Transferring = 7
            }

            private void Awake()
            {
                Invoke(() =>
                {
                    //setup
                    gameObject.layer = 0;
                    //Delay to allow for spawn
                    FerryPos = this.transform;
                    //Set base position of dock
                    Docked.position = FerryPos.position;
                    Docked.rotation = FerryPos.rotation;
                    //Apply Offsets for movement
                    Vector3 closest = Docked.position + (FerryPos.rotation * Vector3.forward) * (100 + config._ServerSettings.ExtendFerryDistance);
                    if (config._ServerSettings.AutoDistance && plugin.FoundIslands != null)
                    {
                        foreach (IslandData foundislands in plugin.FoundIslands)
                        {
                            Vector3 IL = foundislands.location + (Docked.position - foundislands.location) / 2;
                            IL.y = TerrainMeta.WaterMap.GetHeight(Docked.position) + 3f;
                            if (GamePhysics.LineOfSight(Docked.position + ((FerryPos.rotation * Vector3.forward) * 20) + new Vector3(0, 10, 0), IL, -1))
                            {
                                closest = IL;
                                closest.y = 0;
                                if (config._ServerSettings.ShowDebugMsg) plugin.Puts("Found Auto Island @ " + (IL).ToString());
                            }
                        }
                        Departure.position = closest;
                    }
                    Departure.position = closest;
                    Arrival.position = Docked.position + (FerryPos.rotation * Vector3.forward) * 124.2f;
                    Arrival.position = Arrival.position + (FerryPos.rotation * Vector3.right) * 73.8f;
                    Docking.position = Docked.position + (FerryPos.rotation * Vector3.forward) * 49f;
                    Docking.position = Docking.position + (FerryPos.rotation * Vector3.right) * 48.8f;
                    CastingOff.position = Docked.position + (FerryPos.rotation * Vector3.forward) * 24f;
                    CastingOff.position = CastingOff.position + (FerryPos.rotation * Vector3.right) * 3.8f;
                    EjectionZone.position = Docked.position + (FerryPos.rotation * Vector3.forward) * -36.4f;
                    EjectionZone.position = EjectionZone.position + (FerryPos.rotation * Vector3.right) * -2f;
                    Arrival.rotation = Docked.rotation;
                    CastingOff.rotation = Docked.rotation;
                    //Set this server in sync state
                    plugin.UpdateSync(plugin.thisserverip + ":" + plugin.thisserverport, ServerIP + ":" + ServerPort, "Sync");
                    SyncFerrys();
                }, 1f);
            }

            private void OnDestroy()
            {
                enabled = false;
                CancelInvoke();
                if (!IsDestroyed) { Kill(); }
            }

            private void Die()
            {
                if (this != null && !IsDestroyed) { Destroy(this); }
            }

            private void Update()
            {
                //Stops ferry doing anything if not setup/synced
                if (!ServerSynced || ServerIP == "" || ServerPort == "" || _isTransferring) { return; }
                if (this == null)
                {
                    Die();
                    return;
                }
                if (!base.isServer) { return; }
                if (_state == OpenNexusFerry.State.Waiting)
                {
                    //Waits at waiting state
                    if (_sinceStartedWaiting < config._FerrySettings.WaitTime) { return; }
                    SwitchToNextState();
                }
                if (MoveTowardsTarget()) { SwitchToNextState(); }
            }

            //Get position to move to
            private Transform GetTargetTransform(OpenNexusFerry.State state)
            {
                switch (state)
                {
                    case OpenNexusFerry.State.Arrival:
                        return Arrival;
                    case OpenNexusFerry.State.Docking:
                        return Docking;
                    case OpenNexusFerry.State.Stopping:
                    case OpenNexusFerry.State.Waiting:
                        return Docked;
                    case OpenNexusFerry.State.CastingOff:
                        return CastingOff;
                    case OpenNexusFerry.State.Departure:
                        return Departure;
                    default:
                        return base.transform;
                }
            }

            private void TransfereWait()
            {
                if (LoadingSync) return;
                string sqlQuery = "SELECT `state` FROM sync WHERE `sender` = @0 AND `target` = @1;";
                //Read the targets state
                Sql selectCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, ServerIP + ":" + ServerPort, plugin.thisserverip + ":" + plugin.thisserverport);
                plugin.sqlLibrary.Query(selectCommand, plugin.sqlConnection, list =>
                {
                    if (list == null) { return; }
                    foreach (Dictionary<string, object> entry in list)
                    {
                        //If its set to sync we can carry on
                        if (entry["state"].ToString() == "Transferring")
                        {
                            if (config._ServerSettings.ShowDebugMsg) plugin.Puts("SyncTransfere Syned With Other Server @ " + ServerIP + ":" + ServerPort);
                            _state = GetNextState(_state);
                            ServerSynced = true;
                            TransferOpenNexus();
                            retrys = 0;
                            Invoke(() => DataChecker(), 1f);
                            LoadingSync = true;
                            return;
                        }
                    }
                });
                //Rerun again after delay
                Invoke(() =>
                {
                    if (config._ServerSettings.ShowDebugMsg) plugin.Puts("SyncTransfere Waiting For Other Server @ " + ServerIP + ":" + ServerPort);
                    TransfereWait();
                }, config._ServerSettings.ServerDelay);
            }

            private void Progress()
            {
                //delay to allow players to load in.
                Invoke(() =>
                {
                    _state = OpenNexusFerry.State.Arrival;
                    _isTransferring = false;
                }, config._FerrySettings.ProgressDelay);
            }

            //Keeps checking for packets whiles in transfere state or until max retrys
            private void DataChecker()
            {
                if (_isTransferring && retrys < config._FerrySettings.TransfereTime)
                {
                    plugin.MySQLRead(plugin.thisserverip + ":" + plugin.thisserverport, this);
                    Invoke(() => { DataChecker(); }, 1f);
                    retrys++;
                    return;
                }
                Progress();
            }

            //Load DockedEntitys with everything paranted to Ferry
            public void UpdateDockedEntitys() { DockedEntitys = GetFerryContents(); }

            private void SyncFerrys()
            {
                if (LoadingSync) return;
                string sqlQuery = "SELECT `state` FROM sync WHERE `sender` = @0 AND `target` = @1;";
                //Read the targets state
                Sql selectCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, ServerIP + ":" + ServerPort, plugin.thisserverip + ":" + plugin.thisserverport);
                plugin.sqlLibrary.Query(selectCommand, plugin.sqlConnection, list =>
                {
                    if (list == null) { return; }
                    foreach (Dictionary<string, object> entry in list)
                    {
                        //If its set to sync we can carry on
                        if (entry["state"].ToString() == "Sync")
                        {
                            LoadingSync = true;
                            if (config._ServerSettings.ShowDebugMsg) plugin.Puts("SyncFerrys Starting Ferry in 10 secs");
                            //10 sec delay before starting again to let other server get chance to read this ones sync.
                            Invoke(() =>
                            {
                                LoadingSync = false;
                                _state = GetNextState(_state);
                                ServerSynced = true;
                            }, 10f);
                            return;
                        }
                    }
                });
                //Rerun again after delay
                Invoke(() =>
                {
                    if (config._ServerSettings.ShowDebugMsg) plugin.Puts("SyncFerrys Waiting For Other Server @ " + ServerIP + ":" + ServerPort);
                    SyncFerrys();
                }, config._ServerSettings.ServerDelay);
            }

            private void SwitchToNextState()
            {
                if (_state == OpenNexusFerry.State.Departure)
                {
                    if (!_isTransferring)
                    {
                        plugin.UpdateSync(plugin.thisserverip + ":" + plugin.thisserverport, ServerIP + ":" + ServerPort, "Transferring");
                        _state = OpenNexusFerry.State.Transferring;
                        _isTransferring = true;
                        LoadingSync = false;
                        plugin.MessageScreen(config._StatusSettings.WaitingMSG, FerryPos.position, 40f);
                        TransfereWait();
                        //Create a fail safe for if other ferry never arrives.
                        Invoke(() =>
                        {
                            if (_state == OpenNexusFerry.State.Transferring && _isTransferring && !LoadingSync)
                            {
                                plugin.MessageScreen(config._StatusSettings.FailedMSG, FerryPos.position, 40f, config._FerrySettings.TransfereSyncTime - 5);
                                _isTransferring = false;
                                LoadingSync = true;
                                _state = OpenNexusFerry.State.Arrival;
                            }
                        }, config._FerrySettings.TransfereSyncTime);
                    }
                    return;
                }
                if (_state == OpenNexusFerry.State.Stopping)
                {
                    //Force a resync of ferry with its target part.
                    ServerSynced = false;
                    LoadingSync = false;
                    plugin.UpdateSync(plugin.thisserverip + ":" + plugin.thisserverport, ServerIP + ":" + ServerPort, "Sync");
                    SyncFerrys();
                    return;
                }
                //update state
                _state = GetNextState(_state);
                if (_state == OpenNexusFerry.State.Waiting)
                {
                    _sinceStartedWaiting = 0f;
                    plugin.UpdateSync(plugin.thisserverip + ":" + plugin.thisserverport, ServerIP + ":" + ServerPort, "Waiting");
                    return;
                }
                if (_state == OpenNexusFerry.State.Docking)
                {
                    plugin.UpdateSync(plugin.thisserverip + ":" + plugin.thisserverport, ServerIP + ":" + ServerPort, "Docking");
                    UpdateDockedEntitys();
                    return;
                }
                if (_state == OpenNexusFerry.State.CastingOff)
                {
                    //Kick off all the entitys that already been on ferry
                    plugin.EjectEntitys(GetFerryContents(), DockedEntitys, EjectionZone.position);
                    //Delay castoff after eject encase players want to get back on.
                    ServerSynced = false;
                    string CMSG = config._StatusSettings.CastoffMSG.Replace("<$T>", config._FerrySettings.EjectDelay.ToString());
                    plugin.MessageScreen(CMSG, FerryPos.position, 60f);
                    Invoke(() => { ServerSynced = true; }, config._FerrySettings.EjectDelay);
                    plugin.UpdateSync(plugin.thisserverip + ":" + plugin.thisserverport, ServerIP + ":" + ServerPort, "CastingOff");
                    return;
                }
            }

            private OpenNexusFerry.State GetPreviousState(OpenNexusFerry.State currentState)
            {
                if (currentState != OpenNexusFerry.State.Invalid)
                {
                    return currentState - 1;
                }
                return OpenNexusFerry.State.Invalid;
            }

            private OpenNexusFerry.State GetNextState(OpenNexusFerry.State currentState)
            {
                OpenNexusFerry.State state = currentState + 1;
                if (state >= OpenNexusFerry.State.Departure)
                {
                    state = OpenNexusFerry.State.Departure;
                }
                return state;
            }

            //Code to move ferry
            private bool MoveTowardsTarget()
            {
                try
                {
                    Transform targetTransform = GetTargetTransform(_state);
                    Vector3 position = targetTransform.position;
                    Quaternion rotation = targetTransform.rotation;
                    Vector3 position2 = base.transform.position;
                    position.y = position2.y;
                    Vector3 a;
                    float num;
                    (position - position2).ToDirectionAndMagnitude(out a, out num);
                    float num2 = config._FerrySettings.MoveSpeed * Time.deltaTime;
                    float num3 = Mathf.Min(num2, num);
                    Vector3 position3 = position2 + a * num3;
                    Quaternion rotation2 = base.transform.rotation;
                    OpenNexusFerry.State previousState = GetPreviousState(_state);
                    Quaternion rotation4;
                    if (previousState != OpenNexusFerry.State.Invalid)
                    {
                        Transform targetTransform2 = GetTargetTransform(previousState);
                        Vector3 position4 = targetTransform2.position;
                        Quaternion rotation3 = targetTransform2.rotation;
                        position4.y = position2.y;
                        float num4 = Vector3Ex.Distance2D(position4, position);
                        rotation4 = Quaternion.Slerp(rotation, rotation3, num / num4);
                    }
                    else
                    {
                        rotation4 = Quaternion.Slerp(rotation2, rotation, config._FerrySettings.TurnSpeed * Time.deltaTime);
                    }
                    base.transform.SetPositionAndRotation(position3, rotation4);
                    return num3 < num2;
                }
                catch { }
                return false;
            }

            //Builds list of all basenetworkables on the ferry
            public List<BaseNetworkable> GetFerryContents(bool transfere = false)
            {
                List<BaseNetworkable> list = Pool.GetList<BaseNetworkable>();
                foreach (BaseNetworkable baseEntity in children)
                {
                    //excude ferrys turrets
                    if (baseEntity is NPCAutoTurret) continue;
                    //find mounted players
                    if (baseEntity is BaseVehicle)
                    {
                        BaseVehicle bv = baseEntity as BaseVehicle;
                        foreach (BaseVehicle.MountPointInfo allMountPoint in bv.allMountPoints)
                        {
                            if (allMountPoint.mountable != null)
                            {
                                BasePlayer bp = null;
                                bp = allMountPoint.mountable._mounted;
                                if (bp != null) { list.Add(bp); }
                            }
                        }
                    }
                    list.Add(baseEntity);
                }
                if (transfere)
                {
                    //Extra scan incase player is in mid air not paranted or mounted to ferry
                    List<BasePlayer> CatchPlayersJumping = new List<BasePlayer>();
                    Vis.Entities<BasePlayer>(this.transform.position + (this.transform.rotation * Vector3.forward * 6), 14f, CatchPlayersJumping);
                    foreach (BasePlayer bp in CatchPlayersJumping) { if (!list.Contains(bp) && !bp.IsNpc) { list.Add(bp); } }
                    Vis.Entities<BasePlayer>(this.transform.position + (this.transform.rotation * Vector3.forward * -12), 14f, CatchPlayersJumping);
                    foreach (BasePlayer bp in CatchPlayersJumping) { if (!list.Contains(bp) && !bp.IsNpc) { list.Add(bp); } }
                }
                return list;
            }

            //Manage transitioning the player to next server at correct time.
            private void sendplayer(ulong steamid, Network.Connection connection)
            {
                plugin.ReadPlayers(ServerIP + ":" + ServerPort, steamid, connection);
                Invoke(() =>
                {
                    if (plugin.MovePlayers.Contains(steamid))
                    {
                        plugin.MovePlayers.Remove(steamid);
                        BasePlayer bp = BasePlayer.FindAwakeOrSleeping(steamid.ToString());
                        if (bp != null && bp.IsConnected)
                        {
                            plugin.AdjustConnectionScreen(bp, "Open Nexus Switching Server", config._FerrySettings.RedirectDelay);
                            bp.ClientRPCPlayer(null, bp, "StartLoading");
                            bp.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, false);
                            plugin.AdjustConnectionScreen(bp, "Open Nexus Switching Server", config._FerrySettings.RedirectDelay);
                            Invoke(() =>
                            {
                                ConsoleNetwork.SendClientCommand(bp.net.connection, "nexus.redirect", new object[] { ServerIP, ServerPort });
                                bp.ToPlayer().Kick("OpenNexus Moving Server");
                                bp.Kill();
                            }, config._FerrySettings.RedirectDelay);
                            return;
                        }
                    }
                    if (retrys < config._FerrySettings.TransfereTime - 1) { sendplayer(steamid, connection); }
                }
                , 1f);
            }

            private void TransferOpenNexus()
            {
                List<BaseNetworkable> list = new List<BaseNetworkable>();
                //Get all entitys on the  ferry to transfere
                list = GetFerryContents(true);
                //Check if anything to transfere
                if (list.Count != 0)
                {
                    //Create a packet to send to OpenNexus
                    string data = plugin.CreatePacket(list, this);
                    object injection = Interface.CallHook("OnOpenNexusWrite", data);
                    if (injection is string) { data = injection as string; }
                    else if (injection is bool) { return; }
                    if (data == null || data == "") { return; }
                    //Add Compression
                    if (config._ServerSettings.UseCompression) { data = Convert.ToBase64String(Compression.Compress(Encoding.UTF8.GetBytes(data))); }
                    //Write packet
                    plugin.MySQLWrite(ServerIP + ":" + ServerPort, plugin.thisserverip + ":" + plugin.thisserverport, data);
                    if (config._ServerSettings.ShowDebugMsg) plugin.Puts("Written " + String.Format("{0:0.##}", (double)(data.Length / 1024f)) + " Kb");
                    foreach (BaseEntity be in list.ToArray())
                    {
                        //Handle base players
                        BasePlayer bp = be as BasePlayer;
                        if (bp != null && !bp.IsNpc)
                        {
                            plugin.UpdatePlayers(plugin.thisserverip + ":" + plugin.thisserverport, ServerIP + ":" + ServerPort, "Moving", bp.UserIDString);
                            sendplayer(bp.userID, bp.net.connection);
                            continue;
                        }
                        //Destroy the entity since its been sent in packet
                        be.transform.position = new Vector3(0, 0, 0);
                        be.Kill();
                    }
                }
            }
        }

        private class EdgeTeleport : FacepunchBehaviour
        {
            BaseMountable vehicle;
            uint RanMapSize;
            private void Awake()
            {
                //Check if its a allowed entity
                string[] prefab = this.name.Split('/');
                if (plugin.BaseVehicle.Contains(prefab[prefab.Length - 1].Replace(".prefab", ""))) { setup(); }
            }

            private void setup()
            {
                //Stops trying to start when spawned back in on server restarting
                if (Rust.Application.isLoading)
                {
                    Invoke(() => setup(), 10f);
                    return;
                }
                RanMapSize = plugin.RanMapSize;
                vehicle = GetComponent<BaseMountable>();
                InvokeRepeating(Check, 1f, 1f);
            }
            private void OnDestroy() { CancelInvoke(); }
            public void DestroyMe() { Destroy(this); }
            private void Check()
            {
                if (vehicle == null) DestroyMe();
                if (!vehicle.IsMounted()) { return; }
                if (vehicle.transform.position.x > RanMapSize || vehicle.transform.position.x < (RanMapSize * -0.9) || vehicle.transform.position.z > RanMapSize || vehicle.transform.position.z < (RanMapSize * -0.9))
                {
                    vehicle.transform.position = new Vector3(vehicle.transform.position.x * -0.85f, vehicle.transform.position.y, vehicle.transform.position.z * -0.85f);
                    vehicle.TransformChanged();
                    return;
                }
            }
        }
        #endregion
    }
}