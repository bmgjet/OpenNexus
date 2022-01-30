//None of this code is to be used in any paid project/plugins

//ToDo
//Other Vechiles
//Plugin Data

//Setting Up Dock On Map
//Make Custom Prefab group and name it SERVER=ipaddress,PORT=portnumber
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
namespace Oxide.Plugins
{
    [Info("OpenNexus", "bmgjet", "1.0.0")]
    [Description("Nexus system created by bmgjet")]
    public class OpenNexus : RustPlugin
    {
        //Main Settings
        float serverdelay = 2f; //How long between mysql requests leave atleast 500ms (0.5) when used with local server
        string MySQLHost = "localhost"; //IP address of mysql server
        int MySQLPort = 3306;           //Port of mysql server
        string MySQLDB = "OpenNexus";   //Database to use
        string MySQLUsername = "OpenNexus"; //Username to login to mysql server
        string MySQLPassword = "1234";      //Password to login to mysql server (If you get password errors make sure your using mysql 5 not 8)
        public int ExtendFerryDistance = 0; //Extend how far the ferry goes before it triggers transistion

        //Ferry Settings
        public float MoveSpeed = 5f;  //How fast it moves in the water
        public float TurnSpeed = 0.6f; //How fast it can spin around
        public float WaitTime = 60; //How long it waits at dock before leaving
        public int TransfereTime = 60; //Max time it waits out at ocean for a transfer
        public int ProgressDelay = 30; //Extra time it waits after transfere for players to spawn on it
        public int RedirectDelay = 5;  //How long after getting ready flag before it sends user to the next server

        //Advanced Settings
        public string thisserverip = ""; //Over-ride auto detected ip
        public string thisserverport = ""; //Over-ride auto detected port
        public bool UseCompression = true; //Compress Data Packets (Recommended since it 1/4 the size of the data)
        public bool ShowDebugMsg = true; //Outputs info to console
        //End Settings
        private static readonly string permbypass = "OpenNexus.bypass"; //bypass the single server at a time limit
        private static readonly string permadmin = "OpenNexus.admin"; //Allows to use admin commands
        private void AdjustConnectionScreen(BasePlayer player, string msg, int wait) { ServerMgr.Instance.connectionQueue.nextMessageTime = 1; if (Net.sv.write.Start()) { Net.sv.write.PacketID(Message.Type.Message); Net.sv.write.String(msg); Net.sv.write.String("Please wait " + wait + " seconds"); Net.sv.write.Send(new SendInfo(player.Connection)); } }
        public Vector3 StringToVector3(string sVector) { if (sVector.StartsWith("(") && sVector.EndsWith(")")) { sVector = sVector.Substring(1, sVector.Length - 2); } string[] sArray = sVector.Split(','); Vector3 result = new Vector3(float.Parse(sArray[0]), float.Parse(sArray[1]), float.Parse(sArray[2])); return result; }
        public Quaternion StringToQuaternion(string sVector) { if (sVector.StartsWith("(") && sVector.EndsWith(")")) { sVector = sVector.Substring(1, sVector.Length - 2); } string[] sArray = sVector.Split(','); Quaternion result = new Quaternion(float.Parse(sArray[0]), float.Parse(sArray[1]), float.Parse(sArray[2]), float.Parse(sArray[3])); return result; }
        private void StartSleeping(BasePlayer player) { if (!player.IsSleeping()) { Interface.CallHook("OnPlayerSleep", player); player.SetPlayerFlag(BasePlayer.PlayerFlags.Sleeping, true); player.sleepStartTime = Time.time; BasePlayer.sleepingPlayerList.Add(player); player.CancelInvoke("InventoryUpdate"); player.CancelInvoke("TeamUpdate"); player.SendNetworkUpdateImmediate(); } }
        private bool HasPermission(BasePlayer player, string perm) => permission.UserHasPermission(player.UserIDString, perm);
        public List<ulong> ProcessingPlayers = new List<ulong>();
        public List<ulong> MovePlayers = new List<ulong>();
        Core.MySql.Libraries.MySql sqlLibrary = Interface.Oxide.GetLibrary<Core.MySql.Libraries.MySql>();
        Core.Database.Connection sqlConnection;
        public static OpenNexus plugin;

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
            MySQLRead("", null, packetid,player);
        }

        [ConsoleCommand("OpenNexus.Resync")]
        private void cmdResync(ConsoleSystem.Arg arg)
        {
            //Forces all ferrys to wait at dock until set time
            if (!arg.IsAdmin) { return; }
            string sqlQuery = "DROP TABLE IF EXISTS sync;";
            Sql deleteCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery);
            sqlLibrary.ExecuteNonQuery(deleteCommand, sqlConnection);
            if (ShowDebugMsg) Puts("cmdReset Dropped sync table");
        }

        [ConsoleCommand("resettables")]
        private void cmdReset(ConsoleSystem.Arg arg)
        {
            //Resets database tables
            if(!arg.IsAdmin) { return; }

            string sqlQuery = "DROP TABLE IF EXISTS  players, packets, sync;";
            Sql deleteCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery);
            sqlLibrary.ExecuteNonQuery(deleteCommand, sqlConnection);
            if (ShowDebugMsg) Puts("cmdReset Dropped all tables");
            timer.Once(1f, () =>
             {
                 CreatesTables();
                 timer.Once(1f, () =>
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

        //Send Read data from mysql database
        private void MySQLRead(string Target, OpenNexusFerry OpenFerry, int findid = 0, BasePlayer player = null)
        {
            string sqlQuery;
            Sql selectCommand;
            //If player passed its a admin command to read packet
            if (player != null)
            {
                sqlQuery = "SELECT `id`, `spawned`,`data` FROM packets WHERE `id` = @0;";
                selectCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, findid);
            }
            else
            {
                sqlQuery = "SELECT `id`, `spawned`,`data` FROM packets WHERE `target`= @0 AND `sender` = @1 ORDER BY id DESC;";
                selectCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, Target, OpenFerry.ServerIP + ":" + OpenFerry.ServerPort);
            }
            sqlLibrary.Query(selectCommand, sqlConnection, list =>
            {
                if (list == null)
                {
                    return;
                }

                foreach (Dictionary<string, object> entry in list)
                {
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
                    else{data = entry["data"].ToString();}
                    //Admin command
                    if (player != null)
                    {
                        //Paste given packet id back onto ferry
                        foreach (var basenetworkable in BaseNetworkable.serverEntities)
                        {
                            if (basenetworkable.prefabID == 2508295857 && !basenetworkable.IsDestroyed)
                            {
                                //Eject any entitys on the ferry so they dont drop in water
                                OpenNexusFerry Ferry = basenetworkable as OpenNexusFerry;
                                if (Ferry != null)
                                {
                                    foreach (KeyValuePair<string, BaseNetworkable> entity in ReadPacket(data, Ferry, id))
                                    {
                                        Vector3 pos;
                                        BaseEntity be = entity.Value as BaseEntity;
                                        if (be != null)
                                        {
                                            TryFindEjectionPosition(out pos, player.transform.position);
                                            if (be is BasePlayer)
                                            {
                                                plugin.TeleportPlayer((be as BasePlayer), pos);
                                            }
                                            else
                                            {
                                                be.SetParent(null, false, false);
                                                be.ServerPosition = pos;
                                                be.SendNetworkUpdateImmediate(false);
                                            }
                                        }
                                    }
                                    if (plugin.ShowDebugMsg) plugin.Puts("Pasted " + String.Format("{0:0.##}", (double)(entry["data"].ToString().Length / 1024f)) + " Kb");
                                }
                                return;
                            }
                        }
                        return;
                    }
                    //Process mysql packet
                    ReadPacket(data, OpenFerry, id);
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
                //If ID is given then update this
                string sqlQuery = "UPDATE packets SET `spawned` = @1 WHERE `id` = @0;";
                Sql updateCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, id, spawned);
                sqlLibrary.Update(updateCommand, sqlConnection, rowsAffected =>
                {
                    if (rowsAffected > 0)
                    {
                        if (ShowDebugMsg) Puts("MySQLWrite Record Updated");
                        return;
                    }
                    else
                    {
                        if (ShowDebugMsg) Puts("MySQLWrite Record Update Failed!");
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
                        if (ShowDebugMsg) Puts("MySQLWrite New record inserted with ID: {0}", sqlConnection.LastInsertRowId);
                        return;
                    }
                    else
                    {
                        if (ShowDebugMsg) Puts("MySQLWrite Failed to insert!");
                        return;
                    }
                });
            }
        }

        //Maintains table of each ferrys status
        private void UpdateSync(string fromaddress, string target, string state)
        {
            //try Update
            string sqlQuery = "UPDATE sync SET `state` = @0 WHERE `sender` = @1 AND `target` = @2;";
            Sql updateCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, state, fromaddress, target);
            sqlLibrary.Update(updateCommand, sqlConnection, rowsAffected =>
            {
                if (rowsAffected > 0)
                {
                    if (ShowDebugMsg) Puts("UpdateSync Record Updated");
                    return;
                }

                //Update failed so do insert
                sqlQuery = "INSERT INTO sync (`state`, `sender`, `target`) VALUES (@0, @1, @2);";
                Sql insertCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, state, fromaddress, target);
                sqlLibrary.Insert(insertCommand, sqlConnection, rowsAffectedwrite =>
                {
                    if (rowsAffectedwrite > 0)
                    {
                        if (ShowDebugMsg) Puts("UpdateSync New Record inserted with ID: {0}", sqlConnection.LastInsertRowId);
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
                    if (ShowDebugMsg) Puts("ReadPlayers No Player Data For  " + steamid.ToString() + " Creating It");
                    UpdatePlayers(Target, Target, "Playing", steamid.ToString());
                    return;
                }

                foreach (Dictionary<string, object> entry in list)
                {
                    //Redirect player to last server they were in.
                    if (entry["target"].ToString() != thisserverip+":"+thisserverport && entry["state"].ToString() != "Moving" && entry["state"].ToString() != "Ready" && connection != null)
                    {
                        if(BasePlayer.FindByID(steamid).IPlayer.HasPermission(permbypass))
                        {
                            return;
                        }
                        string[] server = entry["target"].ToString().Split(':');
                        if (ShowDebugMsg) Puts("ReadPlayers Redirecting  " + steamid);
                        ConsoleNetwork.SendClientCommand(connection, "nexus.redirect", new object[]
                                    {
                                                server[0],
                                                server[1]
                                    });
                        return;
                    }
                    //Waits for player moving
                    if (entry["state"].ToString() == "Moving")
                    {
                        if (ShowDebugMsg) Puts("ReadPlayers Waiting for server to set player as ready " + steamid);
                        return;
                    }
                    //Sets flag to move player
                    if (entry["state"].ToString() == "Ready")
                    {
                        if (ShowDebugMsg) Puts("ReadPlayers Player Ready to move " + steamid);
                        MovePlayers.Add(ulong.Parse(steamid.ToString()));
                        //Sets flag back to playing
                        UpdatePlayers(Target, Target, "Playing", steamid.ToString());
                        return;
                    }
                    return;
                }
                //Creates new player data
                if (ShowDebugMsg) Puts("No Player Data For  " + steamid + " Creating It");
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
                    if (ShowDebugMsg) Puts("UpdatePlayers Record Updated");
                    Updated = true;
                    return;
                }
            });
            timer.Once(serverdelay / 2, () =>
            {
                if (!Updated)
                {
                    //Failed to update so create new
                    sqlQuery = "INSERT INTO players (`state`, `target`, `sender`,`steamid`) VALUES (@0, @1, @2, @3);";
                    Sql insertCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, state, target, fromaddress, steamid);

                    sqlLibrary.Insert(insertCommand, sqlConnection, rowsAffected =>
                    {
                        if (rowsAffected > 0)
                        {
                            if (ShowDebugMsg) Puts("UpdatePlayers New Record inserted with ID: {0}", sqlConnection.LastInsertRowId);
                        }
                    });
                }
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
            sqlLibrary.Insert(Core.Database.Sql.Builder.Append("CREATE TABLE IF NOT EXISTS `packets` (`id` int(11) unsigned NOT NULL AUTO_INCREMENT, `spawned` int(1) NOT NULL,`timestamp` varchar(64) NOT NULL,`target` varchar(21),`sender` varchar(21),`data` text, PRIMARY KEY (`id`)) DEFAULT CHARSET=utf8;"), sqlConnection);
            sqlLibrary.Insert(Core.Database.Sql.Builder.Append("CREATE TABLE IF NOT EXISTS `sync` (`id` int(11) unsigned NOT NULL AUTO_INCREMENT,`sender` varchar(21),`target` varchar(21),`state` varchar(21), PRIMARY KEY (`id`)) DEFAULT CHARSET=utf8;"), sqlConnection);
            sqlLibrary.Insert(Core.Database.Sql.Builder.Append("CREATE TABLE IF NOT EXISTS `players` (`id` int(11) unsigned NOT NULL AUTO_INCREMENT,`sender` varchar(21),`target` varchar(21),`state` varchar(21),`steamid` varchar(21), PRIMARY KEY (`id`)) DEFAULT CHARSET=utf8;"), sqlConnection);
        }

        //Set up permissions
        private void Init()
        {
            permission.RegisterPermission(permadmin, this);
            permission.RegisterPermission(permbypass, this);
        }

        private void OnServerInitialized(bool initial)
        {
            //Connect to database
            ConnectToMysql(MySQLHost, MySQLPort, MySQLDB, MySQLUsername, MySQLPassword);
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
            plugin = this;

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
                        return;
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
                    if (ferry == null) return;
                    //Attach open nexus code
                    OpenNexusFerry OpenFerry = ferry.gameObject.AddComponent<OpenNexusFerry>();
                    OpenFerry.DockPos = new Vector3(prefabdata.position.x, prefabdata.position.y, prefabdata.position.z);
                    OpenFerry.DockRot = rotation;
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
                    UnityEngine.Object.Destroy(OpenFerry.GetComponent<SavePause>());
                }
            }
        }

        private void OnWorldPrefabSpawned(GameObject gameObject, string str)
        {
            //Remove Uncoded NexusFerry
            BaseEntity component = gameObject.GetComponent<BaseEntity>();
            if (component != null)
            {
                if (component.prefabID == 2508295857 && component.OwnerID == 0)
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
        
        private void Unload()
        {
            //Remove any NexusFerrys
            foreach (var basenetworkable in BaseNetworkable.serverEntities)
            {
                if (basenetworkable.prefabID == 2508295857)
                {
                    //Eject any entitys on the ferry so they dont drop in water
                    OpenNexusFerry Ferry = basenetworkable as OpenNexusFerry;
                    if (Ferry != null)
                    {
                        try
                        {
                            //Dissconnect database and set sync state as offline
                            if (sqlConnection != null && sqlConnection.Con != null)
                            {
                                try
                                {
                                    UpdateSync(thisserverip + ":" + thisserverport, Ferry.ServerIP+":"+Ferry.ServerPort, "Offline");
                                    sqlLibrary.CloseDb(sqlConnection);
                                }
                                catch { }
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

                        }
                        catch { }
                    }
                    try
                    {
                        //Kill the ferry
                        Ferry.Kill();
                    }
                    catch { }
                }
            }
            plugin = null;
        }

        //Process data packet
        public Dictionary<string, BaseNetworkable> ReadPacket(string packet, OpenNexusFerry Ferry, int id)
        {
            //List of entitys that have been recreated
            Dictionary<string, BaseNetworkable> CreatedEntitys = new Dictionary<string, BaseNetworkable>();
            //Checks its a opennexus packet
            if (packet.Contains("<OpenNexus>"))
            {
                //Mark Packet as read
                MySQLWrite("", "", "", id, 1);
                //Hold variables for cars
                Dictionary<string, ModularCar> SpawnedCars = new Dictionary<string, ModularCar>();
                //Deserilize from webpacket
                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, List<Dictionary<string, string>>>>(packet.Replace("<OpenNexus>", ""));
                foreach (KeyValuePair<string, List<Dictionary<string, string>>> packets in data)
                {
                    //Process Packets
                    if (packets.Key.Contains("BasePlayerInventory"))
                    {
                        ProcessItems(packets.Value, CreatedEntitys);
                        continue;
                    }
                    if (packets.Key.Contains("BasePlayer"))
                    {
                        var bp = ProcessBasePlayer(packets.Value, Ferry);
                        if (bp != null)
                        {
                            foreach (KeyValuePair<string, BaseNetworkable> m in bp)
                            {
                                CreatedEntitys.Add(m.Key, m.Value);
                            }
                        }
                        continue;
                    }
                    if (packets.Key.Contains("MiniCopter"))
                    {
                        var mc = ProcessHeli(packets.Value, Ferry);
                        if (mc != null)
                        {
                            foreach (KeyValuePair<string, BaseNetworkable> m in mc)
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
                        var rh = ProcessHorse(packets.Value, Ferry);
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
                        timer.Once(0.5f, () =>
                        {
                            ProcessModuleParts(packets.Value, SpawnedCars, 0);
                        });
                        continue;
                    }
                    if (packets.Key.Contains("ModularCarStorage"))
                    {
                        timer.Once(0.5f, () =>
                        {
                            ProcessModuleParts(packets.Value, SpawnedCars, 1);
                        });
                        continue;
                    }
                    if (packets.Key.Contains("ModularCarCamper"))
                    {
                        timer.Once(0.5f, () =>
                        {
                            ProcessModuleParts(packets.Value, SpawnedCars, 2);
                        });
                        continue;
                    }
                    if (packets.Key.Contains("ModularCarBags"))
                    {
                        timer.Once(0.5f, () =>
                        {
                            ProcessModuleParts(packets.Value, SpawnedCars, int.Parse(packets.Key.Replace("ModularCarBags[", "").Replace("]", "")));
                        });
                        continue;
                    }
                    if (packets.Key.Contains("ModularCar"))
                    {
                        var sc = ProcessCar(packets.Value, Ferry);
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
                Ferry.retrys = 99;
            }
            //Return list of everything created.
            return CreatedEntitys;
        }

        private Dictionary<string, BaseNetworkable> ProcessHeli(List<Dictionary<string, string>> packets, OpenNexusFerry FerryPos)
        {
            //Process mini and scrap heli
            Quaternion rot = new Quaternion();
            Vector3 pos = Vector3.zero;
            string prefab = "";
            float health = 0;
            float maxhealth = 0;
            int fuel = 0;
            ulong ownerid = 0;
            string netid = "";
            foreach (Dictionary<string, string> i in packets)
            {
                foreach (KeyValuePair<string, string> ii in i)
                {
                    switch (ii.Key)
                    {
                        case "rotation":
                            rot = StringToQuaternion(ii.Value);
                            break;
                        case "position":
                            pos = StringToVector3(ii.Value);
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
                        case "netid":
                            netid = ii.Value;
                            break;
                    }
                }
            }
            //Create heli
            MiniCopter minicopter = GameManager.server.CreateEntity(prefab) as MiniCopter;
            if (minicopter == null) return null;
            //spawn and setit up
            minicopter.Spawn();
            minicopter.SetMaxHealth(maxhealth);
            minicopter.health = health;
            if (fuel != 0)
            {
                //Apply fuel ammount
                var fuelContainer = minicopter?.GetFuelSystem()?.GetFuelContainer()?.inventory;
                if (fuelContainer != null)
                {
                    var lowgrade = ItemManager.CreateByItemID(-946369541, fuel);
                    if (!lowgrade.MoveToContainer(fuelContainer))
                    {
                        lowgrade.Remove();
                    }
                }
            }
            minicopter.OwnerID = ownerid;
            minicopter.SetParent(FerryPos);
            minicopter.transform.localPosition = pos;
            minicopter.transform.localRotation = rot;
            minicopter.TransformChanged();
            minicopter.SendNetworkUpdateImmediate();
            return new Dictionary<string, BaseNetworkable>() { { netid, minicopter } };
        }

        private Dictionary<string, ModularCar> ProcessCar(List<Dictionary<string, string>> packets, OpenNexusFerry FerryPos)
        {
            //Process modular car
            Quaternion rot = new Quaternion();
            Vector3 pos = Vector3.zero;
            string prefab = "";
            float health = 0;
            float maxhealth = 0;
            int fuel = 0;
            ulong ownerid = 0;
            string modules = "";
            string conditions = "";
            int slots = 0;
            string netid = "";
            foreach (Dictionary<string, string> i in packets)
            {
                foreach (KeyValuePair<string, string> ii in i)
                {
                    switch (ii.Key)
                    {
                        case "rotation":
                            rot = StringToQuaternion(ii.Value);
                            break;
                        case "position":
                            pos = StringToVector3(ii.Value);
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
                        case "conditions":
                            conditions = ii.Value;
                            break;
                        case "netid":
                            netid = ii.Value;
                            break;
                    }
                }
            }
            //Create car
            ModularCar car = GameManager.server.CreateEntity(prefab) as ModularCar;
            if (car == null) return null;
            //Spawn custom modules
            car.spawnSettings.useSpawnSettings = true;
            //Read module data and get it ready
            AttacheModules(car, modules, conditions);
            car.Spawn();
            //setup health
            car.SetMaxHealth(maxhealth);
            car.health = health;
            string[] Modconditions = conditions.Split('|');
            NextTick(() =>
            {
                //Apply custom modules health
                int cond = 0;
                foreach (BaseVehicleModule vm in car.AttachedModuleEntities)
                {
                    try { vm.health = float.Parse(Modconditions[cond++]); } catch { vm.health = 50f; }
                }
            });
            if (fuel != 0)
            {
                //apply fuel amount
                var fuelContainer = car?.GetFuelSystem()?.GetFuelContainer()?.inventory;
                if (fuelContainer != null)
                {
                    var lowgrade = ItemManager.CreateByItemID(-946369541, fuel);
                    if (!lowgrade.MoveToContainer(fuelContainer))
                    {
                        lowgrade.Remove();
                    }
                }
            }
            car.OwnerID = ownerid;
            car.SetParent(FerryPos);
            car.transform.localPosition = pos;
            car.transform.localRotation = rot;
            car.TransformChanged();
            car.SendNetworkUpdateImmediate();
            return new Dictionary<string, ModularCar>() { { netid, car } };
        }

        //custom modules waiting list
        private ItemModVehicleModule[] CarModules;
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

        private Dictionary<string, BaseNetworkable> ProcessHorse(List<Dictionary<string, string>> packets, OpenNexusFerry FerryPos)
        {
            //process horse
            Quaternion rot = new Quaternion();
            Vector3 pos = Vector3.zero;
            int currentBreed = 0;
            string prefab = "";
            float maxSpeed = 0;
            float maxHealth = 0;
            float health = 0;
            float maxStaminaSeconds = 0;
            float staminaCoreSpeedBonus = 0;
            ulong ownerid = 0;
            string[] flags = new string[3];
            string netid = "";
            foreach (Dictionary<string, string> i in packets)
            {
                foreach (KeyValuePair<string, string> ii in i)
                {
                    switch (ii.Key)
                    {
                        case "rotation":
                            rot = StringToQuaternion(ii.Value);
                            break;
                        case "position":
                            pos = StringToVector3(ii.Value);
                            break;
                        case "currentBreed":
                            currentBreed = int.Parse(ii.Value);
                            break;
                        case "prefab":
                            prefab = ii.Value;
                            break;
                        case "health":
                            health = float.Parse(ii.Value);
                            break;
                        case "maxhealth":
                            maxHealth = float.Parse(ii.Value);
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
                        case "ownerid":
                            ownerid = ulong.Parse(ii.Value);
                            break;
                        case "flags":
                            flags = ii.Value.Split(' ');
                            break;
                        case "netid":
                            netid = ii.Value;
                            break;
                    }
                }
            }
            //create horse
            RidableHorse horse = GameManager.server.CreateEntity(prefab) as RidableHorse;
            if (horse == null) return null;
            horse.Spawn();
            //set its health
            horse.SetMaxHealth(maxHealth);
            horse.SetHealth(health);
            //set its breed and stats
            horse.ApplyBreed(currentBreed);
            horse.maxSpeed = maxSpeed;
            horse.maxStaminaSeconds = maxStaminaSeconds;
            horse.staminaSeconds = maxStaminaSeconds;
            horse.staminaCoreSpeedBonus = staminaCoreSpeedBonus;
            horse.OwnerID = ownerid;
            horse.SetFlag(BaseEntity.Flags.Reserved4, bool.Parse(flags[0]));
            horse.SetFlag(BaseEntity.Flags.Reserved5, bool.Parse(flags[1]));
            horse.SetFlag(BaseEntity.Flags.Reserved6, bool.Parse(flags[2]));
            horse.SetParent(FerryPos);
            horse.transform.localPosition = pos;
            horse.transform.localRotation = rot;
            horse.TransformChanged();
            horse.SendNetworkUpdateImmediate();
            return new Dictionary<string, BaseNetworkable>() { { netid, horse } };
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

        private Dictionary<string, BaseNetworkable> ProcessBasePlayer(List<Dictionary<string, string>> packets, OpenNexusFerry FerryPos)
        {
            //Process BasePlayer
            Quaternion rot = new Quaternion();
            Vector3 pos = Vector3.zero;
            string steamid = "";
            string name = "";
            float health = 0;
            float maxhealth = 0;
            float hydration = 0;
            float calories = 0;
            string mounted = "0";
            int seat = 0;
            foreach (Dictionary<string, string> i in packets)
            {
                foreach (KeyValuePair<string, string> ii in i)
                {
                    switch (ii.Key)
                    {
                        case "rotation":
                            rot = StringToQuaternion(ii.Value);
                            break;
                        case "position":
                            pos = StringToVector3(ii.Value);
                            break;
                        case "steamid":
                            steamid = ii.Value;
                            break;
                        case "name":
                            name = ii.Value;
                            break;
                        case "health":
                            health = float.Parse(ii.Value);
                            break;
                        case "maxhealth":
                            maxhealth = float.Parse(ii.Value);
                            break;
                        case "hydration":
                            hydration = float.Parse(ii.Value);
                            break;
                        case "calories":
                            calories = float.Parse(ii.Value);
                            break;
                        case "mounted":
                            mounted = ii.Value;
                            break;
                        case "seat":
                            seat = int.Parse(ii.Value);
                            break;
                    }
                }
            }
            //Server Server for player already spawned;
            BasePlayer player = BasePlayer.FindAwakeOrSleeping(steamid);
            if (player == null) player = BasePlayer.FindBot(ulong.Parse(steamid)); //Allows BMGbots to chase players across servers
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
            player.Spawn();
            StartSleeping(player);
            player.CancelInvoke(player.KillMessage);
            player.userID = ulong.Parse(steamid);
            player.UserIDString = steamid;
            player.displayName = name;
            player.eyeHistory.Clear();
            player.lastTickTime = 0f;
            player.lastInputTime = 0f;
            player.stats.Init();
            player.InitializeHealth(health, maxhealth);
            player.metabolism.hydration.SetValue(hydration);
            player.metabolism.calories.SetValue(calories);
            DebugEx.Log(string.Format("{0} with steamid {1} joined from OpenNexus", player.displayName, player.userID), StackTraceLogType.None);
            player.SetParent(FerryPos);
            player.transform.localPosition = pos;
            player.transform.localRotation = rot;
            player.TransformChanged();
            //Protect player
            player.SetFlag(BaseEntity.Flags.Protected, false);
            player.SendNetworkUpdateImmediate();
            if (ShowDebugMsg) Puts("ProcessBasePlayer Setting ready flag for player to transition");
            plugin.UpdatePlayers(plugin.thisserverip + ":" + plugin.thisserverport, plugin.thisserverip + ":" + plugin.thisserverport, "Ready", steamid.ToString());
            return new Dictionary<string, BaseNetworkable>() { { steamid, player } };
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
                            GiveContents(ownerid, id, condition, amount, skinid, code, int.Parse(ii.Value),mods, CreatedEntitys);
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
                    if(mods != "")
                    {
                        string[] wmod = mods.Split('|');
                        foreach(string w in wmod)
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
                    if (mod.Contains("="))
                    {
                        string[] modsetting = mod.Split('=');
                        if(ShowDebugMsg)Puts("BuildItem adding mod " + modsetting[0] + " " + modsetting[1]);
                        item.contents.AddItem(CreateItem(int.Parse(modsetting[0]), 1, 0, float.Parse(modsetting[1]), code, 0).info, 1);
                    }
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

        public string CreatePacket(List<BaseNetworkable> Transfere, OpenNexusFerry FerryPos, string dat = "")
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
                        mods += (mod.info.itemid.ToString() + "=" + mod.condition.ToString() + "|");
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

        public Dictionary<string, string> baseVechicle(BaseVehicle bv, OpenNexusFerry FerryPos, string socket = "0", string lockid = "0")
        {
            //baseVechicle packet
            string Modules = "";
            string Conditions = "";
            //Create module and there condition list if car
            ModularCar car = bv as ModularCar;
            if (car != null)
            {
                foreach (var moduleEntity in car.AttachedModuleEntities)
                {
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
                        { "fuel", bv.GetFuelSystem().GetFuelAmount().ToString() },
                        { "lockid", lockid },
                        { "modules", Modules},
                        { "conditions", Conditions},
                        { "netid", bv.net.ID.ToString()},
                        { "sockets", socket },
                        };
            return itemdata;
        }

        public Dictionary<string, string> basePlayer(BasePlayer baseplayer, OpenNexusFerry FerryPos)
        {
            //baseplayer packet
            uint mounted = 0;
            int seat = 0;
            BaseVehicle bv = baseplayer.GetMountedVehicle();
            if (bv != null)
            {
                seat = bv.GetPlayerSeat(baseplayer);
                mounted = bv.net.ID;
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
                        { "mounted", mounted.ToString() },
                        { "seat", seat.ToString() }
                        };
            return itemdata;
        }

        public Dictionary<string, string> basehorse(RidableHorse horse, OpenNexusFerry FerryPos)
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

        //Open Nexus Ferry Code
        public class OpenNexusFerry : BaseEntity
        {
            //Variables
            Transform FerryPos;
            public string ServerIP = "";
            public string ServerPort = "";
            public int retrys = 0;
            public bool ServerSynced = false;
            private bool LoadingSync = false;
            private OpenNexusFerry.State _state = OpenNexusFerry.State.Stopping;
            public Vector3 DockPos;
            public Quaternion DockRot;
            public Transform Docked = new GameObject().transform;
            public Transform Departure = new GameObject().transform;
            public Transform Arrival = new GameObject().transform;
            public Transform Docking = new GameObject().transform;
            public Transform CastingOff = new GameObject().transform;
            public Transform EjectionZone = new GameObject().transform;
            private TimeSince _sinceStartedWaiting;
            public bool _isTransferring;
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
                //setup
                gameObject.layer = 0;
                FerryPos = transform;

                //Delay to allow for spawn
                Invoke(() =>
                {
                    //Set base position of dock
                    Docked.position = FerryPos.position;
                    Docked.rotation = FerryPos.rotation;

                    ////Apply Offsets for movement
                    Departure.position = Docked.position + (FerryPos.rotation * Vector3.forward) * (100 + plugin.ExtendFerryDistance);
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
                if (ServerIP == "" || ServerPort == "" || _isTransferring || !ServerSynced) { return; }
                if (this == null)
                {
                    Die();
                    return;
                }
                if (!base.isServer)
                {
                    return;
                }

                if (_state == OpenNexusFerry.State.Waiting)
                {
                    if (_sinceStartedWaiting < plugin.WaitTime)
                    {
                        //Waits at waiting state
                        return;
                    }
                    SwitchToNextState();
                }
                if (MoveTowardsTarget())
                {
                    SwitchToNextState();
                }
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

            public void UpdateDockedEntitys()
            {
                //Load DockedEntitys with everything paranted to Ferry
                DockedEntitys = GetFerryContents();
            }


            void SyncFerrys()
            {
                if (LoadingSync) return;
                string sqlQuery = "SELECT `state` FROM sync WHERE `sender` = @0 AND `target` = @1;";
                //Read the targets state
                Sql selectCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, ServerIP+":"+ServerPort, plugin.thisserverip+":"+plugin.thisserverport);
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


                            //set it to waiting
                            //sqlQuery = "UPDATE sync SET `state` = @0 WHERE `sender` = @1 AND `target`= @2;";
                            //selectCommand = Oxide.Core.Database.Sql.Builder.Append(sqlQuery, "Waiting", ServerIP + ":" + ServerPort, plugin.thisserverip + ":" + plugin.thisserverport);
                            //plugin.sqlLibrary.Update(selectCommand, plugin.sqlConnection, rowsAffected =>
                            //{
                            //    if (rowsAffected > 0)
                            //    {
                            //        ServerSynced = true;
                            //        if (plugin.ShowDebugMsg) plugin.Puts("SyncFerrys Sync frame read, moving to waiting state @ " + plugin.thisserverip + ":" + plugin.thisserverport);
                            //        return;
                            //    }
                            //});
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
                    plugin.EjectEntitys(GetFerryContents(), DockedEntitys,EjectionZone.position);
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
            public List<BaseNetworkable> GetFerryContents()
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
                list = GetFerryContents();
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
    }
}