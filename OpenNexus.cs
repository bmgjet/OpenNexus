//None of this code is to be used in any paid project/plugins

//TO DO:
//Teams remain across servers
//Final bug checks

//Setting Up Dock On Map
//Make Custom Prefab group and name it SERVER=ipaddress,PORT=portnumber,name
//Make sure to tick Convert selection into group.
//And you might want to place a safezone on it since there isnt one there by default as of AUX01
//If you want to use NexusIsland, Place it on the very edge of the map based of its gizmo not what it looks like. Rotate it as needed
//Dont place the ferry prefab.

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
using UnityEngine.AI;
using Rust;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("OpenNexus", "bmgjet", "1.0.0")]
    [Description("Nexus system created by bmgjet")]
    public class OpenNexus : RustPlugin
    {
        //Main Settings
        float serverdelay = 1f;                //How long between mysql requests leave atleast 2 when used with local server
        string MySQLHost = "localhost";        //IP address of mysql server
        int MySQLPort = 3306;                  //Port of mysql server
        string MySQLDB = "OpenNexus";          //Database to use
        string MySQLUsername = "OpenNexus";    //Username to login to mysql server
        string MySQLPassword = "1234";         //Password to login to mysql server (If you get password errors make sure your using mysql 5 not 8)
        public int ExtendFerryDistance = 0;    //Extend how far the ferry goes before it triggers transistion
        public bool AutoDistance = false;      //Go to nearest island
        public bool SyncTimeWeather = true;    //Syncs all servers to be the same time and weather as first one that joined nexus
        public int SyncTimeWeaterEvery = 300;  //Resyncs to first servers time/weather every seconds
        public bool EdgeTeleporter = true;     //If any BaseVehicle hit edge of server they will teleport back to other side to stop them dying. 

        //Ferry Settings
        public float MoveSpeed = 10f;           //How fast it moves in the water
        public float TurnSpeed = 0.6f;          //How fast it can spin around
        public float WaitTime = 60;             //How long it waits at dock before leaving
        public int TransfereTime = 60;          //Max time it waits out at ocean for a transfer
        public int ProgressDelay = 60;          //Extra time it waits after transfere for players to spawn on it
        public int RedirectDelay = 5;           //How long after getting ready flag before it sends user to the next server

        //Advanced Settings
        public string thisserverip = "";        //Over-ride auto detected ip
        public string thisserverport = "";      //Over-ride auto detected port
        public bool UseCompression = true;      //Compress Data Packets (Recommended since it 1/4 the size of the data)
        public bool ShowDebugMsg = true;        //Outputs info to console
        public string[] BaseVehicle = { "rowboat", "rhib", "scraptransporthelicopter", "minicopter.entity", "submarinesolo.entity", "submarineduo.entity" };
        //End Settings

        //Permissions
        private static readonly string permbypass = "OpenNexus.bypass"; //bypass the single server at a time limit
        private static readonly string permadmin = "OpenNexus.admin";   //Allows to use admin commands

        //Memory
        public Dictionary<Vector3, Quaternion> FoundIslands = new Dictionary<Vector3, Quaternion>();
        public Dictionary<ulong, int> SeatPlayers = new Dictionary<ulong, int>();
        public List<BaseNetworkable> unloadable = new List<BaseNetworkable>();
        public List<ulong> ProcessingPlayers = new List<ulong>();
        public List<ulong> MovePlayers = new List<ulong>();
        private ItemModVehicleModule[] CarModules;
        public static OpenNexus plugin;
        public Climate climate;
        public uint RanMapSize;

        //Plugin Hooks
        [PluginReference]
        private Plugin Backpacks, Economics, ZLevelsRemastered, ServerRewards;

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
        [ConsoleCommand("resettables")]
        private void cmdReset(ConsoleSystem.Arg arg)
        {
            //Resets database tables
            if (!arg.IsAdmin) { return; }

            string sqlQuery = "DROP TABLE IF EXISTS  players, packets, sync;";
            Sql deleteCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery);
            sqlLibrary.ExecuteNonQuery(deleteCommand, sqlConnection);
            if (ShowDebugMsg) Puts("cmdReset Dropped all tables");
            timer.Once(serverdelay, () =>
             {
                 CreatesTables();
                 timer.Once(serverdelay, () =>
                  {
                      //Reset players data
                      foreach (BasePlayer bp in BasePlayer.activePlayerList)
                      {
                          if (bp.IsConnected && !bp.IsNpc)
                          {
                              NextTick(() =>
                              {
                                  UpdatePlayers(thisserverip + ":" + thisserverport, thisserverip + ":" + thisserverport, "Playing", bp.UserIDString);
                              });
                          }
                      }
                  });
             });
        }
        #endregion

        #region MySQL
        //MySQL
        Core.MySql.Libraries.MySql sqlLibrary = Interface.Oxide.GetLibrary<Core.MySql.Libraries.MySql>();
        Core.Database.Connection sqlConnection;

        //Send Read data from mysql database
        private void MySQLRead(string Target, OpenNexusFerry OpenFerry, int findid = 0, BasePlayer player = null)
        {
            string sqlQuery;
            Sql selectCommand;
            //If passed it a admin command to read given packet
            if (player != null)
            {
                sqlQuery = "SELECT `id`, `spawned`,`data` FROM packets WHERE `id` = @0;";
                selectCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, findid);
            }
            else
            {
                //Read last transfered packet waiting for us
                sqlQuery = "SELECT `id`, `spawned`,`data` FROM packets WHERE `target`= @0 AND `sender` = @1 ORDER BY id DESC;";
                selectCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, Target, OpenFerry.ServerIP + ":" + OpenFerry.ServerPort);
            }
            sqlLibrary.Query(selectCommand, sqlConnection, list =>
            {
                if (list == null) { return; }
                foreach (Dictionary<string, object> entry in list)
                {
                    //Packet has already been spawned on server before so dont re-transfere
                    if (entry["spawned"].ToString() != "0")
                    {
                        if (findid == 0)
                        {
                            continue;
                        }
                    }
                    //Process Packet
                    int id = int.Parse(entry["id"].ToString());
                    string data = "";
                    if (UseCompression) { data = Encoding.UTF8.GetString(Compression.Uncompress(Convert.FromBase64String(entry["data"].ToString()))); } //compressed
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
                    foreach(BasePlayer bot in Bots)
                    {
                        if (!IsSteamId(bot.UserIDString))
                        {
                            bot.Kill();
                        }
                    }
                    if (plugin.ShowDebugMsg) plugin.Puts("Read " + String.Format("{0:0.##}", (double)(entry["data"].ToString().Length / 1024f)) + " Kb");
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
                    if (rowsAffected > 0)
                    {
                        if (ShowDebugMsg) { Puts("MySQLWrite Record Updated"); }
                        return;
                    }
                    else
                    {
                        if (ShowDebugMsg) { Puts("MySQLWrite Record Update Failed!"); }
                        return;
                    }
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
                        if (ShowDebugMsg) { Puts("MySQLWrite New record inserted with ID: {0}", sqlConnection.LastInsertRowId); }
                        return;
                    }
                    else
                    {
                        if (ShowDebugMsg) { Puts("MySQLWrite Failed to insert!"); }
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
                if (rowsAffected > 0)
                {
                    if (ShowDebugMsg) { Puts("UpdateSync Record Updated"); }
                    return;
                }

                //Update failed so do insert
                sqlQuery = "INSERT INTO sync (`state`, `sender`, `target`, `climate`) VALUES (@0, @1, @2, @3);";
                Sql insertCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, state, fromaddress, target, plugin.getclimate());
                sqlLibrary.Insert(insertCommand, sqlConnection, rowsAffectedwrite =>
                {
                    if (rowsAffectedwrite > 0)
                    {
                        if (ShowDebugMsg) { Puts("UpdateSync New Record inserted with ID: {0}", sqlConnection.LastInsertRowId); }
                    }
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
                    if (ShowDebugMsg) { Puts("ReadPlayers No Player Data For  " + steamid.ToString() + " Creating It"); }
                    UpdatePlayers(Target, Target, "Playing", steamid.ToString());
                    return;
                }

                foreach (Dictionary<string, object> entry in list)
                {
                    //Redirect player to last server they were in.
                    if (entry["target"].ToString() != thisserverip + ":" + thisserverport && entry["state"].ToString() != "Moving" && entry["state"].ToString() != "Ready" && connection != null)
                    {
                        if (BasePlayer.FindByID(steamid).IPlayer.HasPermission(permbypass)) { return; }
                        string[] server = entry["target"].ToString().Split(':');
                        if (ShowDebugMsg) Puts("ReadPlayers Redirecting  " + steamid);
                        ConsoleNetwork.SendClientCommand(connection, "nexus.redirect", new object[] { server[0], server[1] });
                        return;
                    }
                    //Waits for player moving
                    if (entry["state"].ToString() == "Moving")
                    {
                        if (ShowDebugMsg) { Puts("ReadPlayers Waiting for server to set player as ready " + steamid); }
                        return;
                    }
                    //Sets flag to move player
                    if (entry["state"].ToString() == "Ready")
                    {
                        if (ShowDebugMsg) { Puts("ReadPlayers Player Ready to move " + steamid); }
                        MovePlayers.Add(ulong.Parse(steamid.ToString()));
                        //Sets flag back to playing
                        UpdatePlayers(Target, Target, "Playing", steamid.ToString());
                        return;
                    }
                    return;
                }
                //Creates new player data
                if (ShowDebugMsg) { Puts("No Player Data For  " + steamid + " Creating It"); }
                UpdatePlayers(Target, Target, "Playing", steamid.ToString());
            });
        }

        //Updates players table
        private void UpdatePlayers(string fromaddress, string target, string state, string steamid)
        {
            bool Updated = false;
            //trys to update
            string sqlQuery = "UPDATE players SET `state` = @0, `target` = @1, `sender` = @2 WHERE `steamid` = @3;";
            Sql updateCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, state, target, fromaddress, steamid);
            sqlLibrary.Update(updateCommand, sqlConnection, rowsAffected =>
            {
                if (rowsAffected > 0)
                {
                    if (ShowDebugMsg) { Puts("UpdatePlayers Record Updated"); }
                    Updated = true;
                    return;
                }
                //Failed to update so create new
                sqlQuery = "INSERT INTO players (`state`, `target`, `sender`,`steamid`) VALUES (@0, @1, @2, @3);";
                Sql insertCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, state, target, fromaddress, steamid);
                sqlLibrary.Insert(insertCommand, sqlConnection, rowsAffectedInsert =>
                {
                    if (rowsAffectedInsert > 0)
                    {
                        if (ShowDebugMsg) { Puts("UpdatePlayers New Record inserted with ID: {0}", sqlConnection.LastInsertRowId); }
                    }
                });
            });
        }

        //Connect to mysql database
        private void ConnectToMysql(string host, int port, string database, string username, string password)
        {
            sqlConnection = sqlLibrary.OpenDb(host, port, database, username, password, this);
            if (sqlConnection == null || sqlConnection.Con == null)
            {
                //Failed message
                Puts("Couldn't open the MySQL Database: {0} ", sqlConnection.Con.State.ToString());
            }
            else
            {
                //Connected message and create tables if they dont exsist.
                Puts("MySQL server connected: " + host);
                CreatesTables();
            }
        }

        private void CreatesTables()
        {
            //Setup tables if they dont exsist
            sqlLibrary.Insert(Core.Database.Sql.Builder.Append("CREATE TABLE IF NOT EXISTS `packets` (`id` int(11) unsigned NOT NULL AUTO_INCREMENT, `spawned` int(1) NOT NULL,`timestamp` varchar(64) NOT NULL,`target` varchar(21),`sender` varchar(21),`data` text, PRIMARY KEY (`id`)) DEFAULT CHARSET=utf8;"), sqlConnection);
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
            //Connect to database
            ConnectToMysql(MySQLHost, MySQLPort, MySQLDB, MySQLUsername, MySQLPassword);
            //Get Weather data
            climate = SingletonComponent<global::Climate>.Instance;
            if (EdgeTeleporter)
            {
                RanMapSize = (uint)(World.Size / 1.02);
                if (RanMapSize >= 4000)
                {
                    RanMapSize = 3900;
                }
            }
            plugin = this;
            if (SyncTimeWeather)
            {
                timer.Every(SyncTimeWeaterEvery, () => setclimate());
            }
            if (initial)
            {
                //First start up so delay for everything to be spawned
                Fstartup();
            }
            else
            {
                //Plugin restart not delay needed
                Startup();
            }
        }

        private void OnEntitySpawned(BaseEntity baseEntity)
        {
            //Add edge teleport
            if (EdgeTeleporter)
            {
                BaseVehicle vehicle = baseEntity as BaseVehicle;
                if (vehicle != null) if (vehicle.GetComponent<EdgeTeleport>() == null) { vehicle.gameObject.AddComponent<EdgeTeleport>(); return; }
            }
        }

        private void OnWorldPrefabSpawned(GameObject gameObject, string str)
        {
            //Remove Uncoded NexusFerry
            BaseEntity component = gameObject.GetComponent<BaseEntity>();
            if (component != null)
            {
                //NexusFerry / NexusIsland
                if ((component.prefabID == 2508295857 || component.prefabID == 2795004596) && component.OwnerID == 0)
                {
                    component.Kill();
                }
            }
        }

        private void OnPlayerSetInfo(Network.Connection connection, string name, string value)
        {
            //Limits player to 1 server at a time.
            //Use a list to temp stop this hook firing too often
            if (ProcessingPlayers.Contains(connection.ownerid)) return;
            ProcessingPlayers.Add(connection.ownerid);
            timer.Once(10f, () => ProcessingPlayers.Remove(connection.ownerid));
            if (ShowDebugMsg) Puts("Checking if " + connection.ownerid.ToString() + " is already on any OpenNexus servers");
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
            //Remove transfere protection
            player.SetFlag(BaseEntity.Flags.Protected, false);
        }

        private void Unload()
        {
            //Remove plugin created stuff.
            foreach (BaseNetworkable basenetworkable in unloadable)
            {
                //Freeze compiler some times
                //Destroy island
                if (basenetworkable.prefabID == 2795004596)
                {
                    basenetworkable.Kill();
                }
                //Distroy ferry
                if (basenetworkable.prefabID == 2508295857)
                {
                    //Eject any entitys on the ferry so they dont drop in water
                    OpenNexusFerry Ferry = basenetworkable as OpenNexusFerry;
                    if (Ferry != null)
                    {
                        //Dissconnect database and set sync state as offline
                        if (sqlConnection != null && sqlConnection.Con != null)
                        {
                            NextTick(() =>
                            {
                                try
                                {
                                    UpdateSync(thisserverip + ":" + thisserverport, Ferry.ServerIP + ":" + Ferry.ServerPort, "Offline");
                                    sqlLibrary.CloseDb(sqlConnection);
                                }
                                catch { }
                            });
                        }
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
            if (EdgeTeleporter)
            {
                foreach (BaseNetworkable vehicle in BaseNetworkable.serverEntities)
                {
                    if (vehicle is BaseVehicle)
                    {
                        EdgeTeleport et = vehicle.GetComponent<EdgeTeleport>();
                        if (et != null)
                        {
                            UnityEngine.Object.Destroy(et);
                        }
                    }
                }
            }
            plugin = null;
        }
        private void Loaded()
        {
            if (Backpacks == null) { Puts("Backpacks plugin supported https://github.com/LaserHydra/Backpacks"); }
            if (Economics == null) { Puts("Economics supported https://umod.org/plugins/economics"); }
            if (ZLevelsRemastered == null) { Puts("ZLevels Remastered supported https://umod.org/plugins/zlevels-remastered"); }
            if (ServerRewards == null) { Puts("ServerRewards supported https://umod.org/plugins/server-rewards"); } }
        #endregion

        #region Startup
        private void Fstartup()
        {
            //Waits for fully loaded before running
            timer.Once(10f, () =>
            {
                try
                {
                    if (Rust.Application.isLoading)
                    {
                        //Still starting so run a timer again in 10 sec to check.
                        Fstartup();
                        return;
                    }
                }
                catch { }
                //Starup script now.
                Startup();
            });
        }

        void Startup()
        {
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
                    if (FerrySettings == null || FerrySettings.Length < 2)
                    {
                        Debug.LogError("OpenNexus Dock not setup properly");
                        continue;
                    }
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
                    foreach (string setting in FerrySettings)
                    {
                        string[] tempsetting = setting.Split(':');
                        foreach (string finalsetting in tempsetting)
                        {
                            string settingparsed = finalsetting.ToLower().Replace(":", "");
                            if (settingparsed.Contains("server="))
                            {
                                OpenFerry.ServerIP = settingparsed.Replace("server=", "");
                            }
                            else if (settingparsed.Contains("port="))
                            {
                                OpenFerry.ServerPort = settingparsed.Replace("port=", "");
                            }
                        }
                    }
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
                    if (!FoundIslands.ContainsKey(position))
                    {
                        FoundIslands.Add(position, rotation);
                        unloadable.Add(openNexusIsland);
                        Puts("Found Island");
                    }
                }
            }
            //Add edge teleport
            if (EdgeTeleporter) { foreach (BaseNetworkable vehicle in BaseNetworkable.serverEntities) { if (vehicle is BaseVehicle && vehicle.GetComponent<EdgeTeleport>() == null) vehicle.gameObject.AddComponent<EdgeTeleport>(); } }
        }

        #endregion

        #region functions
        private void AdjustConnectionScreen(BasePlayer player, string msg, int wait) { ServerMgr.Instance.connectionQueue.nextMessageTime = 1; if (Net.sv.write.Start()) { Net.sv.write.PacketID(Message.Type.Message); Net.sv.write.String(msg); Net.sv.write.String("Please wait " + wait + " seconds"); Net.sv.write.Send(new SendInfo(player.Connection)); } }
        public Vector3 StringToVector3(string sVector) { if (sVector.StartsWith("(") && sVector.EndsWith(")")) { sVector = sVector.Substring(1, sVector.Length - 2); } string[] sArray = sVector.Split(','); Vector3 result = new Vector3(float.Parse(sArray[0]), float.Parse(sArray[1]), float.Parse(sArray[2])); return result; }
        public Quaternion StringToQuaternion(string sVector) { if (sVector.StartsWith("(") && sVector.EndsWith(")")) { sVector = sVector.Substring(1, sVector.Length - 2); } string[] sArray = sVector.Split(','); Quaternion result = new Quaternion(float.Parse(sArray[0]), float.Parse(sArray[1]), float.Parse(sArray[2]), float.Parse(sArray[3])); return result; }
        private void StartSleeping(BasePlayer player) { if (!player.IsSleeping()) { Interface.CallHook("OnPlayerSleep", player); player.SetPlayerFlag(BasePlayer.PlayerFlags.Sleeping, true); player.sleepStartTime = Time.time; BasePlayer.sleepingPlayerList.Add(player); player.CancelInvoke("InventoryUpdate"); player.CancelInvoke("TeamUpdate"); player.SendNetworkUpdateImmediate(); } }
        private bool HasPermission(BasePlayer player, string perm) => permission.UserHasPermission(player.UserIDString, perm);
        private bool IsSteamId(string id){ulong userId;if (!ulong.TryParse(id, out userId)) { return false; }return userId > 76561197960265728L;}

        void DestroyGroundComp(BaseEntity ent)
        {
            foreach (var groundmissing in ent.GetComponentsInChildren<DestroyOnGroundMissing>())
            {
                UnityEngine.Object.DestroyImmediate(groundmissing);
            }
            foreach (var groundwatch in ent.GetComponentsInChildren<GroundWatch>())
            {
                UnityEngine.Object.DestroyImmediate(groundwatch);
            }
        }

        void DestroyMeshCollider(BaseEntity ent)
        {
            foreach (var mesh in ent.GetComponentsInChildren<MeshCollider>())
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }


        void AddLock(BaseEntity ent, string data)
        {
            string[] codedata = data.Split(new string[] { "<CodeLock>" }, System.StringSplitOptions.RemoveEmptyEntries);
            string[] wlp = codedata[4].Split(new string[] { "<player>" }, System.StringSplitOptions.RemoveEmptyEntries);
            CodeLock alock = GameManager.server.CreateEntity("assets/prefabs/locks/keypad/lock.code.prefab") as CodeLock;
            alock.Spawn();
            foreach (string wl in wlp)
            {
                alock.whitelistPlayers.Add(ulong.Parse(wl));
            }
            try
            {
                alock.OwnerID = alock.whitelistPlayers[0];
            }
            catch { }
            alock.code = codedata[2];
            alock.SetParent(ent, ent.GetSlotAnchorName(BaseEntity.Slot.Lock));
            alock.transform.localPosition = StringToVector3(codedata[0]);
            alock.transform.localRotation = StringToQuaternion(codedata[1]);
            ent.SetSlot(BaseEntity.Slot.Lock, alock);
            alock.SetFlag(BaseEntity.Flags.Locked, bool.Parse(codedata[3]));
            alock.enableSaving = true;
            alock.SendNetworkUpdateImmediate(true);
        }

        public void MountPlayer(BasePlayer player, int seatnum)
        {
            //Try mount seat given in setting
            List<BaseVehicle> bv = new List<BaseVehicle>();
            Vis.Entities<BaseVehicle>(player.transform.position, 1f, bv);
            try
            {
                foreach (BaseVehicle seat in bv)
                {
                    seat.GetMountPoint(seatnum).mountable.AttemptMount(player, false);
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
                closest_seat.GetComponent<BaseMountable>().AttemptMount(player);
                closest_seat.SendNetworkUpdateImmediate();
                player.SendNetworkUpdateImmediate();
            }
        }

        //Handle moving players
        private void TeleportPlayer(BasePlayer player, Vector3 pos)
        {
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
                player.Teleport(pos);
                if (player.IsConnected)
                {
                    player.SendEntityUpdate();
                }
                player.UpdateNetworkGroup();
                player.SendNetworkUpdateImmediate(false);
            }
            finally
            {
                player.SetServerFall(false);
                player.ForceUpdateTriggers();
            }
        }

        public bool TryFindEjectionPosition(out Vector3 position, Vector3 spawnpos, float radius = 1f)
        {
            //try 10 times or drop on center
            Vector3 position2;
            for (int i = 0; i < 100; i++)
            {
                float num = TerrainMeta.HeightMap.GetHeight(spawnpos);
                position2 = new Vector3(Core.Random.Range(spawnpos.x - 10f, spawnpos.x + 10f), spawnpos.y + 2.5f, Core.Random.Range(spawnpos.z - 10f, spawnpos.z + 10f));
                //Find navmesh
                NavMeshHit hit;
                if (NavMesh.SamplePosition(position2, out hit, 30, -1))
                {
                    if (position2.y < hit.position.y)
                    {
                        position2.y = hit.position.y + 1.1f;
                    }
                }
                if (GamePhysics.CheckSphere(position2, radius, Layers.Mask.Construction | Layers.Server.Deployed | Layers.World | Layers.Server.Players | Layers.Mask.Vehicle_World | Layers.Server.VehiclesSimple | Layers.Server.NPCs, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }
                position = position2;
                return true;
            }
            position = spawnpos + new Vector3(0, 5, 0);
            return false;
        }

        public void EjectEntitys(List<BaseNetworkable> currentcontents, List<BaseNetworkable> DockedEntitys, Vector3 EjectionZone)
        {
            //Ejects anything left on Ferry to dock.

            if (currentcontents == null || currentcontents.Count == 0 || DockedEntitys == null || DockedEntitys.Count == 0)
            {
                return;
            }
            foreach (BaseEntity entity in currentcontents)
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
                            plugin.TeleportPlayer(entity.ToPlayer(), serverPosition);
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
        private string getclimate()
        {
            //Build database of current time/weather
            string current = "";
            current += (TOD_Sky.Instance.Cycle.Year) + "|";
            current += (TOD_Sky.Instance.Cycle.Month) + "|";
            current += (TOD_Sky.Instance.Cycle.Day) + "|";
            current += (TOD_Sky.Instance.Cycle.Hour) + "|";
            current += (climate.WeatherState.Atmosphere.Brightness.ToString()) + "|";
            current += (climate.WeatherState.Atmosphere.Contrast.ToString()) + "|";
            current += (climate.WeatherState.Atmosphere.Directionality.ToString()) + "|";
            current += (climate.WeatherState.Atmosphere.MieMultiplier.ToString()) + "|";
            current += (climate.WeatherState.Atmosphere.RayleighMultiplier.ToString()) + "|";
            current += (climate.WeatherState.Clouds.Attenuation.ToString()) + "|";
            current += (climate.WeatherState.Clouds.Brightness.ToString()) + "|";
            current += (climate.WeatherState.Clouds.Coloring.ToString()) + "|";
            current += (climate.WeatherState.Clouds.Coverage.ToString()) + "|";
            current += (climate.WeatherState.Clouds.Opacity.ToString()) + "|";
            current += (climate.WeatherState.Clouds.Saturation.ToString()) + "|";
            current += (climate.WeatherState.Clouds.Scattering.ToString()) + "|";
            current += (climate.WeatherState.Clouds.Sharpness.ToString()) + "|";
            current += (climate.WeatherState.Clouds.Size.ToString()) + "|";
            current += (climate.Weather.DustChance.ToString()) + "|";
            current += (climate.WeatherState.Atmosphere.Fogginess.ToString()) + "|";
            current += (climate.Weather.FogChance.ToString()) + "|";
            current += (climate.Weather.OvercastChance.ToString()) + "|";
            current += (climate.WeatherState.Rain.ToString()) + "|";
            current += (climate.Weather.RainChance.ToString()) + "|";
            current += (climate.WeatherState.Rainbow.ToString()) + "|";
            current += (climate.Weather.StormChance.ToString()) + "|";
            current += (climate.WeatherState.Thunder.ToString()) + "|";
            current += (climate.WeatherState.Wind.ToString()) + "|";
            return current;
        }

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
                    if (entry["sender"].ToString() == thisserverip + ":" + thisserverport)
                    {
                        if (ShowDebugMsg) Puts("Dont Sync Weather/Time this is first server");
                        return;
                    }
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
                    if (ShowDebugMsg) Puts("Sync Weather/Time with first server");
                    return;
                }
            });
        }

        private string getmodifiers(BasePlayer player)
        {
            //Build database of players current modifiers
            string mods = "";
            if (player != null)
            {
                foreach (Modifier m in player.modifiers.All)
                {
                    mods += (m.Type.ToString()) + ",";
                    mods += (m.Source.ToString()) + ",";
                    mods += (m.Value.ToString()) + ",";
                    mods += (m.Duration.ToString()) + ",";
                    mods += (m.TimeRemaining.ToString()) + "|";
                }
            }
            return mods;
        }

        private void setmodifiers(BasePlayer player, string[] mods)
        {
            //Read from database players modifiers
            if (player != null && mods != null && mods.Length != 0)
            {
                List<ModifierDefintion> md = new List<ModifierDefintion>();
                foreach (string mod in mods)
                {
                    try
                    {
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
                            moddef.duration = float.Parse(m[4]);
                            moddef.value = float.Parse(m[2]);
                            md.Add(moddef);
                        }
                    }
                    catch { }
                }
                player.modifiers.Add(md);
                player.DirtyPlayerState();
            }
        }

        private string getblueprints(BasePlayer player)
        {
            //Build data base al players unloacked blueprints
            string bps = "";
            if (player != null)
            {
                foreach (var blueprint in player.PersistantPlayerInfo.unlockedItems)
                {
                    bps += blueprint + "|";
                }
            }
            return bps;
        }

        private void setblueprints(BasePlayer player, string[] blueprints)
        {
            //Apply unlocked blueprints to player from database
            if (player != null && blueprints != null && blueprints.Length != 0)
            {
                foreach (string blueprint in blueprints)
                {
                    try
                    {
                        int bp = int.Parse(blueprint);
                        if (!player.PersistantPlayerInfo.unlockedItems.Contains(int.Parse(blueprint)))
                        {
                            player.PersistantPlayerInfo.unlockedItems.Add(int.Parse(blueprint));
                        }
                    }
                    catch { }
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
                var lowgrade = ItemManager.CreateByItemID(-946369541, amount);
                if (!lowgrade.MoveToContainer(fuelContainer))
                {
                    lowgrade.Remove();
                }
            }
        }

        private void ApplySettings(BaseVehicle bv, BaseEntity parent, float health, float maxhealth, int fuel, ulong ownerid, Vector3 pos, Quaternion rot)
        {
            //Apply base settings
            bv.SetMaxHealth(maxhealth);
            bv.health = health;
            if (fuel != 0)
            {
                SetupFuel(bv, fuel);
            }
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

        #endregion

        #region DataProcessing

        //Process data packet
        public Dictionary<string, BaseNetworkable> ReadPacket(string packet, BaseEntity parent, int id)
        {
            //List of entitys that have been recreated
            Dictionary<string, BaseNetworkable> CreatedEntitys = new Dictionary<string, BaseNetworkable>();
            //Checks its a opennexus packet
            if (packet.Contains("<OpenNexus>"))
            {
                //Mark Packet as read
                MySQLWrite("", "", "", id, 1);
                BasePlayer admin = null;
                if (parent is BasePlayer)
                {
                    admin = parent as BasePlayer;
                    admin.ChatMessage("Admin Pasting");
                }
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
                            foreach (Dictionary<string, string> i in packets.Value)
                            {
                                foreach (KeyValuePair<string, string> ii in i)
                                {
                                    SetEconomicsData(ii.Key, ii.Value);
                                }
                            }
                            continue;
                        }
                        if (packets.Key.Contains("BasePlayerServerRewardsData"))
                        {
                            foreach (Dictionary<string, string> i in packets.Value)
                            {
                                foreach (KeyValuePair<string, string> ii in i)
                                {
                                    SetServerRewardsData(ii.Key, ii.Value);
                                }
                            }
                            continue;
                        }
                        if (packets.Key.Contains("BasePlayerZLevelsRemasteredData"))
                        {
                            foreach (Dictionary<string, string> i in packets.Value)
                            {
                                foreach (KeyValuePair<string, string> ii in i)
                                {
                                    SetZLevelsRemasteredData(ii.Key, ii.Value);
                                }
                            }
                            continue;
                        }
                        if (packets.Key.Contains("BasePlayerInventory"))
                        {
                            ProcessItems(packets.Value, CreatedEntitys);
                            continue;
                        }
                        if (packets.Key.Contains("BasePlayer"))
                        {
                            var bp = ProcessBasePlayer(packets.Value, parent);
                            if (bp != null)
                            {
                                foreach (KeyValuePair<string, BaseNetworkable> m in bp)
                                {
                                    CreatedEntitys.Add(m.Key, m.Value);
                                }
                            }
                            continue;
                        }
                    }
                    if (packets.Key.Contains("MiniCopter"))
                    {
                        var mc = ProcessHeli(packets.Value, parent);
                        if (mc != null)
                        {
                            foreach (KeyValuePair<string, BaseNetworkable> m in mc)
                            {
                                CreatedEntitys.Add(m.Key, m.Value);
                            }
                        }
                        continue;
                    }
                    if (packets.Key.Contains("BaseBoat"))
                    {
                        var bb = ProcessBoat(packets.Value, parent);
                        if (bb != null)
                        {
                            foreach (KeyValuePair<string, BaseNetworkable> m in bb)
                            {
                                CreatedEntitys.Add(m.Key, m.Value);
                            }
                        }
                        continue;
                    }
                    if (packets.Key.Contains("BaseCrane"))
                    {
                        var bc = ProcessCrane(packets.Value, parent);
                        if (bc != null)
                        {
                            foreach (KeyValuePair<string, BaseNetworkable> m in bc)
                            {
                                CreatedEntitys.Add(m.Key, m.Value);
                            }
                        }
                        continue;
                    }
                    if (packets.Key.Contains("SnowMobile"))
                    {
                        var bc = ProcessSnowmobile(packets.Value, parent);
                        if (bc != null)
                        {
                            foreach (KeyValuePair<string, BaseNetworkable> m in bc)
                            {
                                CreatedEntitys.Add(m.Key, m.Value);
                            }
                        }
                        continue;
                    }
                    if (packets.Key.Contains("BaseSubmarine"))
                    {
                        var bs = ProcessSub(packets.Value, parent);
                        if (bs != null)
                        {
                            foreach (KeyValuePair<string, BaseNetworkable> m in bs)
                            {
                                CreatedEntitys.Add(m.Key, m.Value);
                            }
                        }
                        continue;
                    }
                    if (packets.Key.Contains("BaseHorseInventory"))
                    {
                        ProcessItems(packets.Value, CreatedEntitys);
                        continue;
                    }
                    if (packets.Key.Contains("BaseHorse"))
                    {
                        var rh = ProcessHorse(packets.Value, parent);
                        if (rh != null)
                        {
                            foreach (KeyValuePair<string, BaseNetworkable> r in rh)
                            {
                                CreatedEntitys.Add(r.Key, r.Value);
                            }
                        }
                        continue;
                    }
                    //Delay in these ones to allow car to be spawned
                    if (packets.Key.Contains("ModularCarEngine"))
                    {
                        timer.Once(0.5f, () => { ProcessModuleParts(packets.Value, SpawnedCars, 0); });
                        continue;
                    }
                    if (packets.Key.Contains("ModularCarStorage"))
                    {
                        timer.Once(0.5f, () => { ProcessModuleParts(packets.Value, SpawnedCars, 1); });
                        continue;
                    }
                    if (packets.Key.Contains("ModularCarCamper"))
                    {
                        timer.Once(0.5f, () => { ProcessModuleParts(packets.Value, SpawnedCars, 2); });
                        continue;
                    }
                    if (packets.Key.Contains("ModularCarBags"))
                    {
                        timer.Once(0.5f, () => { ProcessModuleParts(packets.Value, SpawnedCars, int.Parse(packets.Key.Replace("ModularCarBags[", "").Replace("]", ""))); });
                        continue;
                    }
                    if (packets.Key.Contains("ModularCar"))
                    {
                        var sc = ProcessCar(packets.Value, parent);
                        if (sc != null)
                        {
                            foreach (KeyValuePair<string, ModularCar> c in sc)
                            {
                                SpawnedCars.Add(c.Key, c.Value);
                            }
                        }
                        continue;
                    }
                }
                //Max out retry counter to end retrying
                if (parent is OpenNexusFerry)
                {
                    (parent as OpenNexusFerry).retrys = 99;
                }
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
            AttachFamily(car, settings.children);
            string[] Modconditions = settings.conditions.Split('|');
            NextTick(() =>
            {
                //Apply custom modules health
                int cond = 0;
                foreach (BaseVehicleModule vm in car.AttachedModuleEntities)
                {
                    try { vm.health = float.Parse(Modconditions[cond++]); } catch { vm.health = 50f; }
                    if (vm is VehicleModuleEngine)
                    {
                        (vm as VehicleModuleEngine).engine.idleFuelPerSec = 0;
                        (vm as VehicleModuleEngine).engine.maxFuelPerSec = 0;
                    }
                }
            });
            ApplySettings(car, parent, settings.health, settings.maxhealth, settings.fuel, settings.ownerid, settings.pos, settings.rot);
            return new Dictionary<string, ModularCar>() { { settings.netid, car } };
        }

        object OnVehicleModulesAssign(ModularCar car, ItemModVehicleModule[] modulePreset)
        {
            //Car is spawning with custom flag Check if there custom modules waiting
            if (CarModules != null)
            {
                //apply modules to sockets
                int socket = 0;
                foreach (ItemModVehicleModule im in CarModules)
                {
                    modulePreset[socket] = im;
                    socket += im.socketsTaken;
                }
                CarModules = null;
            }
            return null;
        }

        private void AttacheModules(ModularCar modularCar, string Modules, string Conditions)
        {
            //Seperate module settings from packet
            if (modularCar == null) return;
            string[] Modshortnames = Modules.Split('|');
            string[] Modconditions = Conditions.Split('|');
            int conditionslot = 0;
            List<ItemModVehicleModule> mods = new List<ItemModVehicleModule>();
            if (Modshortnames != null && Modshortnames.Length != 0 && Modconditions != null && Modconditions.Length != 0)
            {
                foreach (string shortname in Modshortnames)
                {
                    if (shortname != null && shortname != "")
                    {
                        //create modules
                        Item item = ItemManager.CreateByItemID(int.Parse(shortname), 1, 0);
                        if (item == null) continue;
                        item.condition = float.Parse(Modconditions[conditionslot++]);
                        item.MarkDirty();
                        ItemModVehicleModule component = item.info.GetComponent<ItemModVehicleModule>();
                        if (component == null) continue;
                        mods.Add(component);
                    }
                }
                //load modules into waiting  list
                CarModules = mods.ToArray();
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

        private void ProcessModuleParts(List<Dictionary<string, string>> packets, Dictionary<string, ModularCar> SpawnedCars, int type)
        {
            //Process module car parts
            string ownerid = "0";
            int id = 0;
            float condition = 0;
            int amount = 0;
            ulong skinid = 0;
            int code = 0;
            int slot = 0;
            foreach (Dictionary<string, string> i in packets)
            {
                foreach (KeyValuePair<string, string> ii in i)
                {
                    switch (ii.Key)
                    {
                        //Set bags up
                        case "bags":
                            if (SpawnedCars.ContainsKey(type.ToString()))
                            {
                                List<SleepingBag> Bags = new List<SleepingBag>();
                                foreach (BaseEntity baseEntity in SpawnedCars[type.ToString()].children)
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
                                    string[] BagData = ii.Value.Split(new string[] { "<bag>" }, StringSplitOptions.None);
                                    int bagmodded = 0;
                                    foreach (string bag in BagData)
                                    {
                                        string[] data = bag.Split(new string[] { "<uid>" }, StringSplitOptions.None);
                                        if (data.Length == 2)
                                        {
                                            if (Bags.Count >= bagmodded)
                                            {
                                                Bags[bagmodded].deployerUserID = ulong.Parse(data[1]);
                                                Bags[bagmodded].niceName = data[0];
                                                Bags[bagmodded].SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                                                bagmodded++;
                                            }
                                        }
                                    }
                                }
                            }
                            break; ;
                        case "ownerid":
                            ownerid = ii.Value;
                            break;
                        case "id":
                            id = int.Parse(ii.Value);
                            break;
                        case "condition":
                            condition = float.Parse(ii.Value);
                            break;
                        case "amount":
                            amount = int.Parse(ii.Value);
                            break;
                        case "skinid":
                            skinid = ulong.Parse(ii.Value);
                            break;
                        case "code":
                            code = int.Parse(ii.Value);
                            break;
                        case "slot":
                            if (SpawnedCars.ContainsKey(ownerid))
                            {
                                slot = int.Parse(ii.Value);
                                //One item of packet read apply it to car.
                                ModularCar mc = SpawnedCars[ownerid] as ModularCar;
                                if (mc != null)
                                {
                                    foreach (var moduleEntity in mc.AttachedModuleEntities)
                                    {
                                        switch (type)
                                        {
                                            //Apply engine parts
                                            case 0:
                                                var vehicleModuleEngine = moduleEntity as VehicleModuleEngine;
                                                if (vehicleModuleEngine != null)
                                                {
                                                    var Inventory = vehicleModuleEngine.GetContainer()?.inventory;
                                                    if (Inventory != null)
                                                    {
                                                        Inventory.Insert(CreateItem(id, amount, skinid, condition, code));
                                                    }
                                                }
                                                break;
                                            //Fill stoage with items
                                            case 1:
                                                var vehicleModuleStorage = moduleEntity as VehicleModuleStorage;
                                                if (vehicleModuleStorage != null)
                                                {
                                                    var Inventory = vehicleModuleStorage.GetContainer()?.inventory;
                                                    if (Inventory != null)
                                                    {
                                                        Inventory.Insert(CreateItem(id, amount, skinid, condition, code));
                                                    }
                                                }
                                                break;
                                            //Process camper module
                                            case 2:
                                                var vehicleModuleCamper = moduleEntity as VehicleModuleCamper;
                                                if (vehicleModuleCamper != null)
                                                {
                                                    switch (slot)
                                                    {
                                                        //fill storage box
                                                        case 0:
                                                            CreateItem(id, amount, skinid, condition, code).MoveToContainer(vehicleModuleCamper.activeStorage.Get(true).inventory);
                                                            break;
                                                        case 1:
                                                            //fill locker (Moveto doesnt work for some reason)
                                                            vehicleModuleCamper.activeLocker.Get(true).inventory.Insert(CreateItem(id, amount, skinid, condition, code));
                                                            break;
                                                        case 2:
                                                            //Fill bbq
                                                            CreateItem(id, amount, skinid, condition, code).MoveToContainer(vehicleModuleCamper.activeBbq.Get(true).inventory);
                                                            break;
                                                    }
                                                    //Update entitys
                                                    vehicleModuleCamper.SendNetworkUpdateImmediate();
                                                }
                                                break;
                                        }
                                    }
                                }
                            }
                            break;
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
                //Server Server for player already spawned;
                BasePlayer player = BasePlayer.FindAwakeOrSleeping(settings.steamid);
                if (player == null) player = BasePlayer.FindBot(ulong.Parse(settings.steamid)); //Allows BMGbots to chase players across servers
                if (player != null)
                {
                    //Drop all items on that player where they were asleep since shouldnt be a body when transfereing from ferry
                    if (player.inventory.AllItems().Length != 0)
                    {
                        foreach (Item item in player.inventory.AllItems())
                        {
                            item.DropAndTossUpwards(player.transform.position);
                        }
                    }
                    if (player.IsConnected)
                    {
                        //Some how player is already on server so resync them to ferrys data.
                        plugin.AdjustConnectionScreen(player, "Open Nexus Resyncing Server", 0);
                        player.ClientRPCPlayer(null, player, "StartLoading");
                        ConsoleNetwork.SendClientCommand(player.net.connection, "nexus.redirect", new object[]
                        {
                                                thisserverip,
                                                thisserverport
                        });
                    }
                    player.Kill();
                }
                //Create new player for them to spawn into
                player = GameManager.server.CreateEntity("assets/prefabs/player/player.prefab", FerryPos.transform.position, FerryPos.transform.rotation, true).ToPlayer();
                player.lifestate = BaseCombatEntity.LifeState.Dead;
                player.ResetLifeStateOnSpawn = true;
                player.SetFlag(BaseEntity.Flags.Protected, true);
                player.Spawn();
                StartSleeping(player);
                player.CancelInvoke(player.KillMessage);
                player.userID = ulong.Parse(settings.steamid);
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
                player.SendNetworkUpdateImmediate();
                if (ShowDebugMsg) Puts("ProcessBasePlayer Setting ready flag for player to transition");
                plugin.UpdatePlayers(plugin.thisserverip + ":" + plugin.thisserverport, plugin.thisserverip + ":" + plugin.thisserverport, "Ready", settings.steamid.ToString());
                return new Dictionary<string, BaseNetworkable>() { { settings.steamid, player } };
            }
            return null;
        }

        private void ProcessItems(List<Dictionary<string, string>> packets, Dictionary<string, BaseNetworkable> CreatedEntitys)
        {
            //Process items
            string ownerid = "0";
            int id = 0;
            float condition = 0;
            int amount = 0;
            ulong skinid = 0;
            int code = 0;
            string mods = "";
            foreach (Dictionary<string, string> i in packets)
            {
                foreach (KeyValuePair<string, string> ii in i)
                {
                    switch (ii.Key)
                    {
                        case "ownerid":
                            ownerid = ii.Value;
                            break;
                        case "id":
                            id = int.Parse(ii.Value);
                            break;
                        case "condition":
                            condition = float.Parse(ii.Value);
                            break;
                        case "amount":
                            amount = int.Parse(ii.Value);
                            break;
                        case "skinid":
                            skinid = ulong.Parse(ii.Value);
                            break;
                        case "code":
                            code = int.Parse(ii.Value);
                            break;
                        case "mods":
                            mods = ii.Value;
                            break;
                        case "slot":
                            //One item read from packet
                            GiveContents(ownerid, id, condition, amount, skinid, code, int.Parse(ii.Value), mods, CreatedEntitys);
                            break;
                    }
                }
            }
        }

        private void GiveContents(string ownerid, int id, float condition, int amount, ulong skinid, int code, int slot, string mods, Dictionary<string, BaseNetworkable> CreatedEntitys)
        {
            if (CreatedEntitys.ContainsKey(ownerid))
            {
                //Find what the item belongs to
                var FoundEntity = CreatedEntitys[ownerid];
                if (FoundEntity != null)
                {
                    //Ridable horse items
                    RidableHorse rh = FoundEntity as RidableHorse;
                    if (rh != null)
                    {
                        //put into horse inventory
                        rh.inventory.Insert(CreateItem(id, amount, skinid, condition, code));
                        return;
                    }
                    List<string> Wmods = new List<string>();
                    if (mods != "")
                    {
                        string[] wmod = mods.Split('_');
                        foreach (string w in wmod)
                        {
                            Wmods.Add(w);
                        }
                    }
                    //BasePlayers items
                    BasePlayer bp = FoundEntity as BasePlayer;
                    if (bp != null)
                    {
                        Item item;
                        switch (slot)
                        {
                            //Put back in players inventory
                            case 0:
                                item = BuildItem(id, amount, skinid, condition, code, Wmods);
                                item.MoveToContainer(bp.inventory.containerWear);
                                break;
                            case 1:
                                item = BuildItem(id, amount, skinid, condition, code, Wmods);
                                item.MoveToContainer(bp.inventory.containerBelt);
                                break;
                            case 2:
                                item = BuildItem(id, amount, skinid, condition, code, Wmods);
                                item.MoveToContainer(bp.inventory.containerMain);
                                break;
                        }
                        return;
                    }
                }
            }
        }

        private Item BuildItem(int id, int amount, ulong skin, float condition, int code, List<string> mods)
        {
            Item item = CreateItem(id, amount, skin, condition, code);
            //Setup guns so they work when given to player
            var weapon = item.GetHeldEntity() as BaseProjectile;
            if (weapon != null)
            {
                (item.GetHeldEntity() as BaseProjectile).primaryMagazine.contents = (item.GetHeldEntity() as BaseProjectile).primaryMagazine.capacity;

            }
            //Set up mods on gun
            if (mods != null)
            {
                foreach (var mod in mods)
                {
                    try
                    {
                        if (mod.Contains("="))
                        {
                            string[] modsetting = mod.Split('=');
                            if (ShowDebugMsg) Puts("BuildItem adding mod " + modsetting[0] + " " + modsetting[1]);
                            item.contents.AddItem(CreateItem(int.Parse(modsetting[0]), 1, 0, float.Parse(modsetting[1]), code, 0).info, 1);
                        }
                    }
                    catch { }
                }
            }
            return item;
        }

        private Item CreateItem(int id, int amount, ulong skinid, float condition, int code, int blueprintTarget = 0)
        {
            //Create a item
            Item item = ItemManager.Create(ItemManager.FindItemDefinition(id), amount, skinid);
            if (blueprintTarget != 0)
                item.blueprintTarget = blueprintTarget;
            item.condition = condition;
            //Extra checks
            ExtraChecks(item, code);
            item.MarkDirty();
            return item;
        }

        private Item ExtraChecks(Item item, int code)
        {
            //Reapply the code to keys
            if (item.info.shortname == "car.key" || item.info.shortname == "door.key")
            {
                ProtoBuf.Item.InstanceData keycode = new ProtoBuf.Item.InstanceData();
                keycode.dataInt = code;
                item.instanceData = keycode;
            }
            //Need to check other items, maybe notes ect
            return item;
        }

        public string CreatePacket(List<BaseNetworkable> Transfere, BaseEntity FerryPos, string dat = "")
        {
            //Create packet to send to hub
            var data = new Dictionary<string, List<Dictionary<string, string>>>();
            var itemlist = new List<Dictionary<string, string>>();
            //Loop though all basenetworkables that are parented to ferry
            foreach (BaseNetworkable entity in Transfere)
            {
                //Check for bags only one flag
                bool donebags = false;
                //Create a baseplayer packet
                BasePlayer baseplayer = entity as BasePlayer;
                if (baseplayer?.inventory != null)
                {
                    itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(basePlayer(baseplayer, FerryPos));
                    data.Add("BasePlayer[" + baseplayer.UserIDString + "]", itemlist);
                    itemlist = new List<Dictionary<string, string>>();
                    foreach (var item in baseplayer.inventory.containerWear.itemList.ToArray())
                    {
                        itemlist.Add(Contents(item, baseplayer.UserIDString, "0"));
                        item.Remove();
                    }
                    foreach (var item in baseplayer.inventory.containerBelt.itemList.ToArray())
                    {
                        itemlist.Add(Contents(item, baseplayer.UserIDString, "1"));
                        item.Remove();
                    }
                    foreach (var item in baseplayer.inventory.containerMain.itemList.ToArray())
                    {
                        itemlist.Add(Contents(item, baseplayer.UserIDString, "2"));
                        item.Remove();
                    }
                    if (itemlist.Count != 0)
                    {
                        data.Add("BasePlayerInventory[" + baseplayer.UserIDString + "]", itemlist);

                    }
                    if (Backpacks != null) { data.Add("BasePlayerBackpackData[" + baseplayer.UserIDString + "]", GetBackpackData(baseplayer.UserIDString)); }
                    if (Economics != null) { data.Add("BasePlayerEconomicsData[" + baseplayer.UserIDString + "]", GetEconomicsData(baseplayer.UserIDString)); }
                    if (ZLevelsRemastered != null) { data.Add("BasePlayerZLevelsRemasteredData[" + baseplayer.UserIDString + "]", GetZLevelsRemasteredData(baseplayer.UserIDString)); }
                    if (ServerRewards) { data.Add("BasePlayerServerRewardsData[" + baseplayer.UserIDString + "]", GetServerRewardsData(baseplayer.UserIDString)); }
                    //Show Open Nexus Screen
                    if (FerryPos is BasePlayer)
                    {
                        //Is admin command so dont do transfere screen.
                    }
                    else
                    {
                        plugin.AdjustConnectionScreen(baseplayer, "Open Nexus Transfering Data", 10);
                        baseplayer.ClientRPCPlayer(null, baseplayer, "StartLoading");
                        baseplayer.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, false);
                    }
                }
                //Create a horse packet
                RidableHorse horse = entity as RidableHorse;
                if (horse != null)
                {
                    itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(basehorse(horse, FerryPos));
                    data.Add("BaseHorse[" + horse.net.ID.ToString() + "]", itemlist);
                    itemlist = new List<Dictionary<string, string>>();
                    int slot = 0;
                    foreach (Item item in horse.inventory.itemList.ToArray())
                    {
                        itemlist.Add(Contents(item, horse.net.ID.ToString(), slot++.ToString()));
                        item.Remove();
                    }
                    data.Add("BaseHorseInventory[" + horse.net.ID.ToString() + "]", itemlist);
                }
                //create helicopter packet
                MiniCopter helicopter = entity as MiniCopter;
                if (helicopter != null)
                {
                    itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(baseVechicle(helicopter, FerryPos));
                    data.Add("MiniCopter[" + helicopter.net.ID + "]", itemlist);
                }
                //create boat packet
                BaseBoat boat = entity as BaseBoat;
                if (boat != null)
                {
                    itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(baseVechicle(boat, FerryPos));
                    data.Add("BaseBoat[" + boat.net.ID + "]", itemlist);
                }
                //create magnet crane packet
                BaseCrane crane = entity as BaseCrane;
                if (crane != null)
                {
                    itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(baseVechicle(crane, FerryPos));
                    data.Add("BaseCrane[" + crane.net.ID + "]", itemlist);
                }
                //create snowmobile packet
                Snowmobile sm = entity as Snowmobile;
                if (sm != null)
                {
                    itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(baseVechicle(sm, FerryPos));
                    data.Add("SnowMobile[" + sm.net.ID + "]", itemlist);
                }
                //create magnet sub packet
                BaseSubmarine sub = entity as BaseSubmarine;
                if (sub != null)
                {
                    itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(baseVechicle(sub, FerryPos));
                    data.Add("BaseSubmarine[" + sub.net.ID + "]", itemlist);
                }
                //create modular car packet
                ModularCar car = entity as ModularCar;
                if (car != null)
                {
                    string thiscarID = car.net.ID.ToString();
                    itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(baseVechicle(car, FerryPos, car.TotalSockets.ToString(), car.carLock.LockID.ToString()));
                    data.Add("ModularCar[" + thiscarID + "]", itemlist);
                    int socket = 0;
                    List<Item> EngineParts = new List<Item>();
                    List<Item> StorageContainer = new List<Item>();
                    foreach (var moduleEntity in car.AttachedModuleEntities)
                    {
                        itemlist = new List<Dictionary<string, string>>();
                        var vehicleModuleEngine = moduleEntity as VehicleModuleEngine;
                        var vehicleModuleStorage = moduleEntity as VehicleModuleStorage;
                        var vehicleModuleCamper = moduleEntity as VehicleModuleCamper;
                        //Create packet of engine parts
                        if (vehicleModuleEngine != null)
                        {
                            var engineInventory = vehicleModuleEngine.GetContainer()?.inventory;
                            if (engineInventory != null)
                            {
                                foreach (Item item in engineInventory.itemList)
                                {
                                    EngineParts.Add(item);
                                    itemlist.Add(Contents(item, thiscarID, socket.ToString()));
                                }
                                if (itemlist.Count != 0) { data.Add("ModularCarEngine[" + vehicleModuleEngine.net.ID.ToString() + "]", itemlist); }
                            }
                        }
                        if (vehicleModuleStorage != null)
                        {
                            //create packet of storage box items
                            var storageInventory = vehicleModuleStorage.GetContainer()?.inventory;
                            if (storageInventory != null)
                            {
                                itemlist = new List<Dictionary<string, string>>();
                                foreach (Item item in storageInventory.itemList)
                                {
                                    if (!EngineParts.Contains(item))
                                    {
                                        itemlist.Add(Contents(item, thiscarID, socket.ToString()));
                                        StorageContainer.Add(item);
                                    }
                                }
                                if (itemlist.Count != 0) { data.Add("ModularCarStorage[" + vehicleModuleStorage.net.ID.ToString() + "]", itemlist); }
                            }
                        }
                        if (vehicleModuleCamper != null)
                        {
                            //create packet of camper module parts
                            var camperInventory = vehicleModuleCamper.GetContainer()?.inventory;
                            if (camperInventory != null)
                            {
                                itemlist = new List<Dictionary<string, string>>();
                                foreach (Item item in vehicleModuleCamper.activeStorage.Get(true).inventory.itemList)
                                {
                                    if (!EngineParts.Contains(item) && !StorageContainer.Contains(item))
                                    {
                                        StorageContainer.Add(item);
                                        itemlist.Add(Contents(item, thiscarID, "0"));
                                    }
                                }
                                foreach (Item item in vehicleModuleCamper.activeLocker.Get(true).inventory.itemList)
                                {
                                    if (!EngineParts.Contains(item) && !StorageContainer.Contains(item))
                                    {
                                        StorageContainer.Add(item);
                                        itemlist.Add(Contents(item, thiscarID, "1"));
                                    }
                                }
                                foreach (Item item in vehicleModuleCamper.activeBbq.Get(true).inventory.itemList)
                                {
                                    if (!EngineParts.Contains(item) && !StorageContainer.Contains(item))
                                    {
                                        StorageContainer.Add(item);
                                        itemlist.Add(Contents(item, thiscarID, "2"));
                                    }
                                }
                                if (itemlist.Count != 0) { data.Add("ModularCarCamper[" + vehicleModuleCamper.net.ID.ToString() + "]", itemlist); }
                            }
                        }
                        if (!donebags)
                        {
                            //Find all bags for this entity
                            itemlist = new List<Dictionary<string, string>>();
                            string bags = "";
                            foreach (BaseEntity baseEntity in car.children)
                            {
                                if (baseEntity == null) continue;
                                foreach (BaseEntity baseEntity2 in baseEntity.children)
                                {
                                    if (baseEntity2 == null) continue;
                                    SleepingBag sleepingBagCamper;
                                    if ((sleepingBagCamper = (baseEntity2 as SleepingBag)) != null)
                                    {
                                        bags += sleepingBagCamper.niceName + "<uid>" + sleepingBagCamper.deployerUserID.ToString() + "<bag>";
                                    }
                                }
                            }
                            //Check it found some to create its packet
                            if (bags != "") { itemlist.Add(new Dictionary<string, string> { { "bags", bags } }); }
                            if (itemlist.Count != 0) { data.Add("ModularCarBags[" + car.net.ID.ToString() + "]", itemlist); }
                            donebags = true;
                        }
                        socket++;
                    }
                    //Remove items since will be killing entity on transfere and dont want loot bags dropping
                    foreach (Item olditem in EngineParts) { olditem.Remove(); }
                    foreach (Item olditem in StorageContainer) { olditem.Remove(); }
                }
            }
            //Seralize and tag as OpenNexus packet
            dat += "<OpenNexus>" + Newtonsoft.Json.JsonConvert.SerializeObject(data);
            return dat;
        }

        public List<Dictionary<string, string>> GetBackpackData(string Owner)
        {
            List<Dictionary<string, string>> Data = new List<Dictionary<string, string>>();
            ItemContainer Backpack;
            //Gets data with API
            Backpack = Backpacks?.Call<ItemContainer>("API_GetBackpackContainer", ulong.Parse(Owner));
            //If has data
            if (Backpack != null)
            {
                int s = 0;
                //Create list of items in backpack
                foreach (Item item in Backpack.itemList)
                {
                    Data.Add(Contents(item, Owner, s++.ToString()));
                }
            }
            return Data;
        }

        public void SetBackpacksData(string key, List<Dictionary<string, string>> packets)
        {
            if (Backpacks != null)
            {
                ItemContainer Backpack;
                //Gets data with API
                string owneridparsed = key.Replace("]", "").Replace("BasePlayerBackpackData[", "");
                Backpack = Backpacks?.Call<ItemContainer>("API_GetBackpackContainer", ulong.Parse(owneridparsed));
                if (Backpack != null)
                {
                    string ownerid = "0";
                    int id = 0;
                    float condition = 0;
                    int amount = 0;
                    ulong skinid = 0;
                    int code = 0;
                    List<string> Wmods = new List<string>();
                    //Parse out data
                    foreach (Dictionary<string, string> i in packets)
                    {
                        foreach (KeyValuePair<string, string> ii in i)
                        {
                            switch (ii.Key)
                            {
                                case "ownerid":
                                    ownerid = ii.Value;
                                    break;
                                case "id":
                                    id = int.Parse(ii.Value);
                                    break;
                                case "condition":
                                    condition = float.Parse(ii.Value);
                                    break;
                                case "amount":
                                    amount = int.Parse(ii.Value);
                                    break;
                                case "skinid":
                                    skinid = ulong.Parse(ii.Value);
                                    break;
                                case "code":
                                    code = int.Parse(ii.Value);
                                    break;
                                case "mods":
                                    if (ii.Value != "")
                                    {
                                        string[] wmod = ii.Value.Split('_');
                                        foreach (string w in wmod)
                                        {
                                            Wmods.Add(w);
                                        }
                                    }
                                    break;
                                case "slot":
                                    //One item read from packet so create item and put it in backpack
                                    BuildItem(id, amount, skinid, condition, code, Wmods).MoveToContainer(Backpack, int.Parse(ii.Value));
                                    break;
                            }
                        }
                    }
                }
            }
        }

        //Reads players server rewards balance
        public List<Dictionary<string, string>> GetServerRewardsData(string Owner)
        {
            List<Dictionary<string, string>> Data = new List<Dictionary<string, string>>();
            object rwpoints = ServerRewards?.Call<object>("CheckPoints", ulong.Parse(Owner));
            if (rwpoints != null)
            {
                Data.Add(new Dictionary<string, string> { { Owner, rwpoints.ToString() } });
                ServerRewards?.Call<object>("TakePoints", ulong.Parse(Owner), int.Parse(rwpoints.ToString()));
            }
            return Data;
        }

        //Sets players server rewards balance
        public void SetServerRewardsData(string Owner, string Balance)
        {
            if (ServerRewards != null && Owner != null && Balance != null)
            {
                ServerRewards?.Call<string>("AddPoints", Owner, int.Parse(Balance));
            }
        }

        //Reads players Economics balance
        public List<Dictionary<string, string>> GetEconomicsData(string Owner)
        {
            List<Dictionary<string, string>> Data = new List<Dictionary<string, string>>();
            Data.Add(new Dictionary<string, string> { { Owner, Economics?.Call<string>("Balance", Owner) } });
            return Data;
        }

        //Sets players Economics balance
        public void SetEconomicsData(string Owner, string Balance)
        {
            if (Economics != null && Owner != null && Balance != null)
            {
                Economics?.Call<string>("SetBalance", Owner, double.Parse(Balance));
            }
        }

        public List<Dictionary<string, string>> GetZLevelsRemasteredData(string Owner)
        {
            List<Dictionary<string, string>> Data = new List<Dictionary<string, string>>();
            string pinfo = ZLevelsRemastered?.Call<string>("api_GetPlayerInfo", ulong.Parse(Owner));
            if (pinfo != null || pinfo != "")
            {
                Data.Add(new Dictionary<string, string> { { Owner, pinfo } });
            }
            return Data;
        }

        public void SetZLevelsRemasteredData(string ownerid, string packet)
        {
            if (ZLevelsRemastered != null)
            {
                bool updated = ZLevelsRemastered.Call<bool>("api_SetPlayerInfo", ulong.Parse(ownerid), packet);
            }
        }

        public Dictionary<string, string> Contents(Item item, string Owner, string slot = "0")
        {
            //Item packet
            string code = "0";
            string mods = "";
            if (item.info.shortname == "car.key" || item.info.shortname == "door.key")
            {
                //Add keys code data
                code = (item.instanceData.dataInt.ToString());
            }
            //Create list of mods on weapon
            if (item.contents != null)
            {
                foreach (var mod in item.contents.itemList)
                {
                    if (mod.info.itemid != 0)
                    {
                        mods += (mod.info.itemid.ToString() + "=" + mod.condition.ToString() + "_");
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
                        { "mods", mods},
                        { "slot", slot }
                        };
            return itemdata;
        }

        public Dictionary<string, string> baseVechicle(BaseVehicle bv, BaseEntity FerryPos, string socket = "0", string lockid = "0")
        {
            //baseVechicle packet
            string Mounts = "";
            if (bv != null)
            {
                foreach (BaseVehicle.MountPointInfo m in bv.mountPoints)
                {
                    Mounts += "<mount>" + m.pos.ToString() + "&" + m.rot.ToString() + "&" + m.mountable.transform.localRotation.ToString();
                }
            }
            string unlimitedfuel = "False";

            MiniCopter mc = bv as MiniCopter;
            if (mc != null && mc.fuelPerSec == 0)
            {
                unlimitedfuel = "True";
            }
            MotorRowboat boat = bv as MotorRowboat;
            if (boat != null && boat.fuelPerSec == 0)
            {
                unlimitedfuel = "True";
            }
            BaseCrane crane = bv as BaseCrane;
            if (crane != null && crane.fuelPerSec == 0)
            {
                unlimitedfuel = "True";
            }
            BaseSubmarine sub = bv as BaseSubmarine;
            if (sub != null && sub.maxFuelPerSec == 0)
            {
                unlimitedfuel = "True";
            }
            Snowmobile sm = bv as Snowmobile;
            if (sm != null && sm.maxFuelPerSec == 0)
            {
                unlimitedfuel = "True";
            }

            string Modules = "";
            string Conditions = "";
            //Create module and there condition list if car
            ModularCar car = bv as ModularCar;
            if (car != null)
            {
                foreach (var moduleEntity in car.AttachedModuleEntities)
                {
                    if (moduleEntity is VehicleModuleEngine)
                    {
                        if ((moduleEntity as VehicleModuleEngine).engine.maxFuelPerSec == 0)
                        {
                            unlimitedfuel = "True";
                        }
                    }
                    Modules += moduleEntity.AssociatedItemInstance.info.itemid + "|";
                    Conditions += moduleEntity._health + "|";
                }
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
                        { "children", GetAllFamily(bv) + Mounts},
                        { "conditions", Conditions},
                        { "unlimitedfuel", unlimitedfuel},
                        { "netid", bv.net.ID.ToString()},
                        { "sockets", socket },
                        };
            return itemdata;
        }

        public Dictionary<string, string> basePlayer(BasePlayer baseplayer, BaseEntity FerryPos)
        {
            //baseplayer packet
            int seat = 0;
            BaseVehicle bv = baseplayer.GetMountedVehicle();
            if (bv != null)
            {
                seat = bv.GetPlayerSeat(baseplayer);
            }
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
                        { "seat", seat.ToString() },
                        { "mounted", baseplayer.isMounted.ToString() }
                        };
            return itemdata;
        }

        public Dictionary<string, string> basehorse(RidableHorse horse, BaseEntity FerryPos)
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

        void AddChilditems(BaseEntity ent, string data)
        {
            IItemContainerEntity ice = ent as IItemContainerEntity;
            if (ice != null)
            {
                string[] itmlist = data.Split(new string[] { "<item>" }, System.StringSplitOptions.RemoveEmptyEntries);
                int id = 0;
                int amount = 0;
                ulong skin = 0;
                float condition = 0;
                int code = 0;
                List<string> mods = new List<string>();
                foreach (string st in itmlist)
                {
                    string[] info = st.Split(',');
                    if (info.Length != 2)
                    {
                        continue;
                    }
                    switch (info[0])
                    {
                        case "id":
                            id = int.Parse(info[1]);
                            break;
                        case "condition":
                            condition = float.Parse(info[1]);
                            break;
                        case "amount":
                            amount = int.Parse(info[1]);
                            break;
                        case "skinid":
                            skin = ulong.Parse(info[1]);
                            break;
                        case "code":
                            code = int.Parse(info[1]);
                            break;
                        case "mods":
                            if (info[1] != "")
                            {
                                string[] wmod = info[1].Split('_');
                                foreach (string w in wmod)
                                {
                                    mods.Add(w);
                                }
                            }
                            break;
                        case "slot":
                            int slot = 0;
                            try
                            {
                                slot = int.Parse(info[1]);
                            }
                            catch { Puts(info[1]); }
                            Item citem = BuildItem(id, amount, skin, condition, code, mods);
                            if (ShowDebugMsg) Puts("Created child item " + citem.ToString());
                            ice.inventory.Insert(citem);
                            //citem.MoveToContainer(ice.inventory, slot);
                            break;
                    }
                }
            }
        }

        private void AttachFamily(BaseEntity parent, string stringdata)
        {
            if (stringdata == null || stringdata == "") { return; }
            Dictionary<uint, uint> RemapNetID = new Dictionary<uint, uint>();
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

                                if (!baseMountable.enableSaving)
                                {
                                    baseMountable.EnableSaving(true);
                                }
                                if (bminfo.bone != "")
                                {
                                    baseMountable.SetParent(bv, bminfo.bone, true, true);
                                }
                                else
                                {
                                    baseMountable.SetParent(bv, false, false);
                                }
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
            string[] child;
            if (children.Length == 0)
            {
                child = stringdata.Split(new string[] { "<Child>" }, System.StringSplitOptions.RemoveEmptyEntries);
            }
            else
            {
                child = children[0].Split(new string[] { "<Child>" }, System.StringSplitOptions.RemoveEmptyEntries);
            }
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
                    //Update remap
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
                if (cd[11] == "DestroyOnGroundMissing")
                {
                    DestroyGroundComp(e);
                }
                if (cd[12] == "DestroyMeshCollider")
                {
                    DestroyMeshCollider(e);
                }
                e.Spawn();
                if (!RemapNetID.ContainsKey(oldnetid))
                {
                    RemapNetID.Add(oldnetid, e.net.ID);
                }
                BaseNetworkable p = BaseNetworkable.serverEntities.Find(RemapNetID[uint.Parse(cd[15])]);
                e.SetParent(p as BaseEntity);
                e.transform.localPosition = pos;
                e.transform.localRotation = rot;
                e.transform.localScale = StringToVector3(cd[4]);
                e.skinID = ulong.Parse(cd[5]);
                SetFlags(e, cd[8]);
                StabilityEntity s = e as StabilityEntity;
                if (s != null && cd[9] != "null")
                {
                    s.grounded = bool.Parse(cd[9]);
                }
                BaseCombatEntity c = e as BaseCombatEntity;
                if (c != null)
                {
                    c.InitializeHealth(float.Parse(cd[7]), float.Parse(cd[6]));
                    if (cd[10] != "null")
                    {
                        c.pickup.enabled = bool.Parse(cd[10]);
                    }
                }
                if (cd[13] != "null")
                {
                    AddLock(e, cd[13]);
                }
                if (cd[14] != "null")
                {
                    AddChilditems(e, cd[14]);
                }
                e.SendNetworkUpdateImmediate();
            }
            parent.SendNetworkUpdateImmediate();
        }

        private void SetFlags(BaseEntity be, string data)
        {
            string[] f = data.Split(new string[] { "<BEFlag>" }, System.StringSplitOptions.RemoveEmptyEntries);
            List<bool> flags = new List<bool>();
            foreach (string s in f)
            {
                try
                {
                    flags.Add(bool.Parse(s));
                }
                catch { }
            }
            be.SetFlag(BaseEntity.Flags.Broken, flags[0]);
            be.SetFlag(BaseEntity.Flags.Busy, flags[1]);
            be.SetFlag(BaseEntity.Flags.Debugging, flags[2]);
            be.SetFlag(BaseEntity.Flags.Disabled, flags[3]);
            be.SetFlag(BaseEntity.Flags.Locked, flags[4]);
            be.SetFlag(BaseEntity.Flags.On, flags[5]);
            be.SetFlag(BaseEntity.Flags.OnFire, flags[6]);
            be.SetFlag(BaseEntity.Flags.Open, flags[7]);
            if (be is Door)
            {
                NextTick(() =>
                {
                    (be as Door).SetOpen(be.flags.HasFlag(BaseEntity.Flags.Open));
                });
            }
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

        public string GetBaseEntity(BaseEntity be, bool parent = false)
        {
            string baseentity = "";
            //Use net id so can relink
            if (parent) { baseentity += "<Parent>" + be.net.ID + "|"; }
            else { baseentity += "<Child>" + be.net.ID + "|"; }
            baseentity += be.prefabID.ToString() + "|";
            baseentity += be.transform.localPosition.ToString() + "|";
            baseentity += be.transform.localRotation.ToString() + "|";
            baseentity += be.transform.localScale.ToString() + "|";
            baseentity += be.skinID.ToString() + "|";
            baseentity += be.MaxHealth().ToString() + "|";
            baseentity += be.Health().ToString() + "|";
            //Get all flags
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Broken);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Busy);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Debugging);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Disabled);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Locked);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.On);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.OnFire);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Open);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Placeholder);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Protected);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Reserved1);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Reserved10);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Reserved11);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Reserved2);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Reserved3);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Reserved4);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Reserved5);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Reserved6);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Reserved7);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Reserved8);
            baseentity += "<BEFlag>" + be.HasFlag(BaseEntity.Flags.Reserved9);
            baseentity += "|";
            //Check if ground
            StabilityEntity s = be as StabilityEntity;
            if (s == null)
            {
                baseentity += "null|";
            }
            else
            {
                baseentity += s.grounded.ToString() + "|";
            }
            //Check if can pickup
            BaseCombatEntity c = be as BaseCombatEntity;
            if (c == null)
            {
                baseentity += "null|";
            }
            else
            {
                baseentity += c.pickup.enabled.ToString() + "|";
            }

            if (!be.GetComponent<DestroyOnGroundMissing>())
            {
                baseentity += "DestroyOnGroundMissing|";
            }
            else
            {
                baseentity += "null|";
            }
            if (!be.GetComponent<MeshCollider>())
            {
                baseentity += "DestroyMeshCollider|";
            }
            else
            {
                baseentity += "null|";
            }
            if (be.GetSlot(0))
            {
                CodeLock codelock = be.GetSlot(0) as CodeLock;
                if (codelock != null)
                {
                    baseentity += "<CodeLock>" + codelock.transform.localPosition;
                    baseentity += "<CodeLock>" + codelock.transform.localRotation;
                    baseentity += "<CodeLock>" + codelock.code;
                    baseentity += "<CodeLock>" + codelock.HasFlag(CodeLock.Flags.Locked);
                    baseentity += "<CodeLock>";
                    foreach (ulong id in codelock.whitelistPlayers)
                    {
                        baseentity += "<player>" + id;
                    }
                    baseentity += "|";
                }
                else
                {
                    baseentity += "null|";
                }
            }
            else
            {
                baseentity += "null|";
            }
            IItemContainerEntity ic = be as IItemContainerEntity;
            if (ic == null)
            {
                baseentity += "null|";
            }
            else
            {
                 if (ic.inventory != null && ic.inventory.itemList != null)
                {
                    int sl = 0;
                    List<Dictionary<string, string>> itmlist = new List<Dictionary<string, string>>();
                    List<Item> added = new List<Item>();
                    foreach (Item item in ic.inventory.itemList)
                    {
                        if(item == null || added.Contains(item)) { continue; }
                        added.Add(item);
                        itmlist.Add(Contents(item, be.OwnerID.ToString(), sl++.ToString()));
                        item.Remove();
                    }
                    foreach (Dictionary<string, string> dict in itmlist)
                    {
                        foreach (KeyValuePair<string, string> itz in dict)
                        {
                            baseentity += itz.Key + "," + itz.Value + "<item>";
                        }
                    }
                }
                baseentity += "|";
            }
            //Get parents netid so can relink
            BaseEntity p = be.GetParentEntity();
            if (p == null)
            {
                baseentity += "null";
            }
            else
            {
                baseentity += be.GetParentEntity().net.ID.ToString();
            }
            return baseentity;
        }

        private string GetAllFamily(BaseEntity parent)
        {
            List<BaseEntity> Family = new List<BaseEntity>();
            string XML = GetBaseEntity(parent, true);
            foreach (BaseEntity be in parent.children)
            {
                //Stop getting stuk in reference loop
                if (Family.Contains(be)) { continue; }
                //Add to list
                Family.Add(be);
                XML += GetBaseEntity(be);
                if (be.children != null || be.children.Count != 0)
                {
                    //Found children of chidren so run on them
                    GetAllFamily(be);
                }
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
            public string prefab = "";
            public float health = 0;
            public float maxhealth = 0;
            public int fuel = 0;
            public ulong ownerid = 0;
            public string modules = "";
            public string children = "";
            public string conditions = "";
            public int slots = 0;
            public string netid = "";
            public int currentBreed = 0;
            public float maxStaminaSeconds = 0;
            public float staminaCoreSpeedBonus = 0;
            public string[] flags = new string[20];
            public float maxSpeed = 0;
            public string steamid = "";
            public string name = "";
            public float hydration = 0;
            public float calories = 0;
            public bool mounted = false;
            public int seat = 0;
            public bool unlimitedfuel = false;
            public string[] blueprints = new string[0];
            public string[] mods = new string[0];

            public void ProcessPacket(List<Dictionary<string, string>> packets)
            {
                foreach (Dictionary<string, string> i in packets)
                {
                    foreach (KeyValuePair<string, string> ii in i)
                    {
                        switch (ii.Key)
                        {
                            case "rotation":
                                rot = plugin.StringToQuaternion(ii.Value);
                                break;
                            case "position":
                                pos = plugin.StringToVector3(ii.Value);
                                break;
                            case "prefab":
                                prefab = ii.Value;
                                break;
                            case "health":
                                health = float.Parse(ii.Value);
                                break;
                            case "maxhealth":
                                maxhealth = float.Parse(ii.Value);
                                break;
                            case "fuel":
                                fuel = int.Parse(ii.Value);
                                break;
                            case "ownerid":
                                ownerid = ulong.Parse(ii.Value);
                                break;
                            case "slots":
                                slots = int.Parse(ii.Value);
                                break;
                            case "modules":
                                modules = ii.Value;
                                break;
                            case "unlimitedfuel":
                                unlimitedfuel = bool.Parse(ii.Value);
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
                                maxSpeed = float.Parse(ii.Value);
                                break;
                            case "maxStaminaSeconds":
                                maxStaminaSeconds = float.Parse(ii.Value);
                                break;
                            case "staminaCoreSpeedBonus":
                                staminaCoreSpeedBonus = float.Parse(ii.Value);
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
                                hydration = float.Parse(ii.Value);
                                break;
                            case "calories":
                                calories = float.Parse(ii.Value);
                                break;
                            case "blueprints":
                                blueprints = ii.Value.Split('|');
                                break;
                            case "modifiers":
                                mods = ii.Value.Split('_');
                                break;
                            case "seat":
                                seat = int.Parse(ii.Value);
                                break;
                            case "mounted":
                                mounted = (ii.Value.ToLower().Contains("true"));
                                if (!plugin.SeatPlayers.ContainsKey(ulong.Parse(steamid)))
                                {
                                    plugin.SeatPlayers.Add(ulong.Parse(steamid), seat);
                                }
                                break;
                        }
                    }
                }
            }
        }

        //Open Nexus Ferry Code
        public class OpenNexusIsland : BaseEntity
        {
            public global::GameObjectRef MapMarkerPrefab;
            public Transform MapMarkerLocation;
        }

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
            public bool _isTransferring = false;
            public List<BaseNetworkable> DockedEntitys = new List<BaseNetworkable>();

            //Avaliable States
            public enum State
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
                    if (plugin.AutoDistance)
                    {
                        Vector3 closest = Vector3.zero;
                        foreach (KeyValuePair<Vector3, Quaternion> foundislands in plugin.FoundIslands)
                        {
                            if (closest == Vector3.zero)
                            {
                                closest = foundislands.Key;
                            }
                            if (Vector3.Distance(FerryPos.position, foundislands.Key) < Vector3.Distance(FerryPos.position, closest))
                            {
                                closest = foundislands.Key;
                            }
                        }
                        closest.y = 0;
                        if (plugin.ShowDebugMsg) plugin.Puts("Found nearest island @ " + closest.ToString());
                        Departure.position = closest;
                    }
                    else
                    {
                        Departure.position = Docked.position + (FerryPos.rotation * Vector3.forward) * (100 + plugin.ExtendFerryDistance);
                    }
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
                },1f);
            }

            private void OnDestroy()
            {
                enabled = false;
                CancelInvoke();
                if (!IsDestroyed)
                    Kill();
            }

            private void Die()
            {
                if (this != null && !IsDestroyed)
                    Destroy(this);
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
                    if (_sinceStartedWaiting < plugin.WaitTime)
                    {
                        //Waits at waiting state
                        return;
                    }
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

            public void Progress()
            {
                //delay to allow players to load in.
                Invoke(() =>
                {
                    _state = OpenNexusFerry.State.Arrival;
                    _isTransferring = false;
                }, plugin.ProgressDelay);
            }

            //Keeps checking for packets whiles in transfere state or until max retrys
            public void DataChecker()
            {
                if (_isTransferring && retrys < plugin.TransfereTime)
                {
                    plugin.MySQLRead(plugin.thisserverip + ":" + plugin.thisserverport, this);
                    Invoke(() => { DataChecker(); }, 1f);
                    retrys++;
                    return;
                }
                retrys = 0;
                Progress();
            }

            //Load DockedEntitys with everything paranted to Ferry
            public void UpdateDockedEntitys() { DockedEntitys = GetFerryContents(); }

            void SyncFerrys()
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
                            if (plugin.ShowDebugMsg) plugin.Puts("SyncFerrys Starting Ferry in 10 secs");
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
                    if (plugin.ShowDebugMsg) plugin.Puts("SyncFerrys Waiting For Other Server @ " + ServerIP + ":" + ServerPort);
                    SyncFerrys();
                }, plugin.serverdelay);
            }

            private void SwitchToNextState()
            {
                if (_state == OpenNexusFerry.State.Departure)
                {
                    if (!_isTransferring)
                    {
                        plugin.UpdateSync(plugin.thisserverip + ":" + plugin.thisserverport, ServerIP + ":" + ServerPort, "Transferring");
                        TransferOpenNexus();
                        Invoke(() => DataChecker(), 1f);
                    }
                    return;
                }
                if (_state == OpenNexusFerry.State.Stopping)
                {
                    //Force a resync of ferry with its target part.
                    ServerSynced = false;
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
                    plugin.UpdateSync(plugin.thisserverip + ":" + plugin.thisserverport, ServerIP + ":" + ServerPort, "CastingOff");
                    //Kick off all the entitys that already been on ferry
                    plugin.EjectEntitys(GetFerryContents(), DockedEntitys, EjectionZone.position);
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
            public bool MoveTowardsTarget()
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
                    float num2 = plugin.MoveSpeed * Time.deltaTime;
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
                        rotation4 = Quaternion.Slerp(rotation2, rotation, plugin.TurnSpeed * Time.deltaTime);
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
                    List<BasePlayer> CatchPlayersJumping = new List<BasePlayer>();
                    Vis.Entities<BasePlayer>(this.transform.position + (this.transform.rotation * Vector3.forward * 6), 14f, CatchPlayersJumping);
                    foreach (BasePlayer bp in CatchPlayersJumping){if (!list.Contains(bp) && !bp.IsNpc){list.Add(bp);}}
                    Vis.Entities<BasePlayer>(this.transform.position + (this.transform.rotation * Vector3.forward * -12), 14f, CatchPlayersJumping);
                    foreach (BasePlayer bp in CatchPlayersJumping){if (!list.Contains(bp) && !bp.IsNpc) {list.Add(bp);}}
                }
                return list;
            }

            //Manage transitioning the player to next server at correct time.
            void sendplayer(ulong steamid, Network.Connection connection)
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
                            plugin.AdjustConnectionScreen(bp, "Open Nexus Switching Server", plugin.RedirectDelay);
                            bp.ClientRPCPlayer(null, bp, "StartLoading");
                            bp.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, false);
                            plugin.AdjustConnectionScreen(bp, "Open Nexus Switching Server", plugin.RedirectDelay);
                            Invoke(() =>
                            {
                                ConsoleNetwork.SendClientCommand(bp.net.connection, "nexus.redirect", new object[]
                                {
                                                ServerIP,
                                                ServerPort
                                });
                                bp.ToPlayer().Kick("OpenNexus Moving Server");
                                bp.Kill();
                            }, plugin.RedirectDelay);
                            return;
                        }
                    }
                    if (retrys < plugin.TransfereTime - 1)
                    {
                        sendplayer(steamid, connection);
                    }
                }
                , 1f);
            }

            private void TransferOpenNexus()
            {
                _isTransferring = true;
                List<BaseNetworkable> list = new List<BaseNetworkable>();
                _state = OpenNexusFerry.State.Transferring;
                //Get all entitys on the  ferry to transfere
                list = GetFerryContents(true);
                //Check if anything to transfere
                if (list.Count != 0)
                {
                    //Create a packet to send to OpenNexus
                    string data = "";
                    if (plugin.UseCompression)
                    {
                        data = Convert.ToBase64String(Compression.Compress(Encoding.UTF8.GetBytes(plugin.CreatePacket(list, this))));
                    }
                    else
                    {
                        data = plugin.CreatePacket(list, this);
                    }
                    plugin.MySQLWrite(ServerIP + ":" + ServerPort, plugin.thisserverip + ":" + plugin.thisserverport, data);
                    if (plugin.ShowDebugMsg) plugin.Puts("Written " + String.Format("{0:0.##}", (double)(data.Length / 1024f)) + " Kb");
                    foreach (BaseEntity be in list)
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
                if (plugin.BaseVehicle.Contains(prefab[prefab.Length - 1].Replace(".prefab", "")))
                {
                    setup();
                }
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
                if (!vehicle.IsMounted())
                    return;

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