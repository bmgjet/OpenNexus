//None of this code is to be used in any paid project/plugins

//ToDo
//Weapon Mods
//Other Vechiles
//Plugin Data

//Setting Up Dock On Map
//Make Custom Prefab group and name it SERVER=ipaddress,PORT=portnumber
//Make sure to tick Convert selection into group.
//And you might want to place a safezone on it since there isnt one there by default as of AUX01
//If you want to use NexusIsland, Place it on the very edge of the map based of its center not what it looks like. Rotate it to it offset goes past the edge
//Dont place the ferry prefab.

using Facepunch;
using Facepunch.Utility;
using Network;
using Oxide.Core;
using Oxide.Core.Libraries;
using ProtoBuf;
using Rust.Modular;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Time = UnityEngine.Time;
namespace Oxide.Plugins
{
    [Info("OpenNexus", "bmgjet", "1.0.0")]
    [Description("Nexus system created by bmgjet")]
    public class OpenNexus : RustPlugin
    {
        //Main Settings
        public string OpenNexusHub = ""; //Address of opennexus php script
        public string OpenNexusKey = ""; //Key to limit access to only your server
        public int ExtendFerryDistance = 0; ////Extend how far the ferry goes before it triggers transistion

        //Advanced Settings
        public string thisserverip = ""; //Over-ride auto detected ip
        public string thisserverport = ""; //Over-ride auto detected port
        public bool UseCompression = true; //Compress Data Packets (Recommended since it 1/4 the size of the data)
        //End Settings
        public bool IsBase64String(string s) { s = s.Trim(); return (s.Length % 4 == 0) && Regex.IsMatch(s, @"^[a-zA-Z0-9\+/]*={0,3}$", RegexOptions.None); }
        private void AdjustConnectionScreen(BasePlayer player, string msg) { if (Net.sv.write.Start()) { Net.sv.write.PacketID(Message.Type.Message); Net.sv.write.String(msg); Net.sv.write.Send(new SendInfo(player.Connection)); } }
        public Vector3 StringToVector3(string sVector) { if (sVector.StartsWith("(") && sVector.EndsWith(")")) { sVector = sVector.Substring(1, sVector.Length - 2); } string[] sArray = sVector.Split(','); Vector3 result = new Vector3(float.Parse(sArray[0]), float.Parse(sArray[1]), float.Parse(sArray[2])); return result; }
        public Quaternion StringToQuaternion(string sVector) { if (sVector.StartsWith("(") && sVector.EndsWith(")")) { sVector = sVector.Substring(1, sVector.Length - 2); } string[] sArray = sVector.Split(','); Quaternion result = new Quaternion(float.Parse(sArray[0]), float.Parse(sArray[1]), float.Parse(sArray[2]), float.Parse(sArray[3])); return result; }
        private void StartSleeping(BasePlayer player){if (!player.IsSleeping()){Interface.CallHook("OnPlayerSleep", player);player.SetPlayerFlag(BasePlayer.PlayerFlags.Sleeping, true);player.sleepStartTime = Time.time;BasePlayer.sleepingPlayerList.Add(player);player.CancelInvoke("InventoryUpdate");player.CancelInvoke("TeamUpdate");player.SendNetworkUpdateImmediate();}}
        public static OpenNexus plugin;
        private bool setup;
        private void OnServerInitialized(bool initial)
        {
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
                    if (FerrySettings == null || FerrySettings.Length != 2)
                    {
                        Puts("OpenNexus Dock not setup properly");
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
                    OpenFerry.prefabID = ferry.prefabID;
                    OpenFerry.syncPosition = true;
                    OpenFerry.globalBroadcast = true;
                    UnityEngine.Object.DestroyImmediate(ferry);
                    OpenFerry.enableSaving = false;
                    OpenFerry.Spawn();
                    UnityEngine.Object.Destroy(OpenFerry.GetComponent<SavePause>());
                    //Setup with setting in dock prefab name
                    foreach (string setting in FerrySettings)
                    {
                        string[] tempsetting = setting.Split(':');
                        foreach (string finalsetting in tempsetting)
                        {
                            if (finalsetting.ToLower().Contains("server="))
                            {
                                OpenFerry.ServerIP = finalsetting.ToLower().Replace("server=", "");
                            }
                            else if (finalsetting.ToLower().Contains("port="))
                            {
                                OpenFerry.ServerPort = finalsetting.ToLower().Replace("port=", "");
                            }
                        }
                    }
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

        private void Unload()
        {
            //Remove any NexusFerrys
            foreach (var basenetworkable in BaseNetworkable.serverEntities)
            {
                if (basenetworkable.prefabID == 2508295857 && !basenetworkable.IsDestroyed)
                {
                    //Eject any entitys on the ferry so they dont drop in water
                    OpenNexusFerry Ferry = basenetworkable as OpenNexusFerry;
                    if (Ferry != null)
                    {
                        Ferry.UpdateDockedEntitys();
                        Ferry.EjectEntitys();
                    }
                    basenetworkable.Kill();
                }
            }
            //Send Unsync command to server
            UnSyncServers();
            //Remove static reference
            plugin = null;
        }

        public bool ServerSynced = false;
        public bool HasPacket = false;
        private void SyncServers()
        {
            //Ping DataPacket
            string Datapacket = "PING";
            Dictionary<string, string> headers = new Dictionary<string, string> { { "Content-Length", Datapacket.Length.ToString() }, { "User-Agent", OpenNexusKey }, { "FROM", thisserverip + "-" + thisserverport }, { "TARGET", "null" }, { "CMD", "PING" } };
            webrequest.Enqueue(plugin.OpenNexusHub, Datapacket, (code, response) =>
            {
                if (response == null || code != 200)
                {
                    timer.Once(10f, () => SyncServers());
                    Puts("No Reply From OpenNexus Hub");
                    return;
                }
                else
                {
                    if (response == "WAIT")
                    {
                        //Connected but need more then 1 server connected
                        timer.Once(2f, () => { Puts("Waiting for atleast 2 servers on OpenNexus Hub"); SyncServers(); });
                        return;
                    }
                    string[] TimeStamps = response.Split(' ');
                    if (TimeStamps.Length != 2) { Puts("Bad TimeStamp Format in ping"); timer.Once(10f, () => SyncServers()); return; }

                    try
                    {
                        float SyncTime = (int.Parse(TimeStamps[0]) + 30f) - int.Parse(TimeStamps[1]);
                        if (SyncTime < 0)
                        {
                            Puts("Bad TimeStamp Value Retrying in 20sec");
                            timer.Once(10f, () => { UnSyncServers(); });
                            timer.Once(20f, () => { SyncServers(); });
                            return;
                        }
                        Puts("Setting Sync Frame in " + SyncTime.ToString() + " Secs");
                        timer.Once(SyncTime, () => { plugin.Puts("OpenNexus Synced"); ServerSynced = true; });
                        return;
                    }
                    catch
                    { }
                    timer.Once(5f, () => { Puts("Bad Sync Packet"); SyncServers(); });
                }
            }, this, RequestMethod.POST, headers, 10f);
        }

        private void UnSyncServers()
        {
            //Ping DataPacket
            string Datapacket = "";
            Dictionary<string, string> headers = new Dictionary<string, string> { { "Content-Length", Datapacket.Length.ToString() }, { "User-Agent", OpenNexusKey }, { "FROM", thisserverip + "-" + thisserverport }, { "TARGET", "null" }, { "CMD", "LEAVE" } };
            webrequest.Enqueue(plugin.OpenNexusHub, Datapacket, (code, response) =>
            { }, this, RequestMethod.POST, headers, 10f);
        }

        private void Init()
        {
            //Set settings have been entered
            if (OpenNexusHub == "" || OpenNexusKey == "")
            {
                Puts("OpenNexus Plugin hasnt been setup correctly!!!");
                setup = false;
                return;
            }
            setup = true;
        }

        public void ReadPacket(string packet, OpenNexusFerry Ferry)
        {
            //Checks its a opennexus packet
            if (packet.Contains("<OpenNexus>"))
            {
                HasPacket = true;
                //Hold variables
                Dictionary<string, BaseNetworkable> CreatedEntitys = new Dictionary<string, BaseNetworkable>();
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
            }
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
                    try{vm.health = float.Parse(Modconditions[cond++]);}catch{vm.health = 50f;}
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
                                foreach (global::BaseEntity baseEntity in SpawnedCars[type.ToString()].children)
                                {
                                    if (baseEntity == null) continue;
                                    foreach (global::BaseEntity baseEntity2 in baseEntity.children)
                                    {
                                        if (baseEntity2 == null) continue;
                                        SleepingBag sleepingBagCamper;
                                        if ((sleepingBagCamper = (baseEntity2 as global::SleepingBag)) != null)
                                        {
                                            Bags.Add(sleepingBagCamper);
                                        }
                                    }
                                }
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
                                                Bags[bagmodded].SendNetworkUpdate(global::BasePlayer.NetworkQueue.Update);
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
                    }
                }
            }
            //Server Server for player already spawned;
            BasePlayer player = BasePlayer.FindAwakeOrSleeping(steamid);
            if (player == null) player = BasePlayer.FindBot(ulong.Parse(steamid)); //Allows BMGbots to chase players across servers
            if (player != null)
            {
                //Drop all items on that player where they were asleep
                if (player.inventory.AllItems().Length != 0)
                {
                    foreach (Item item in player.inventory.AllItems())
                    {
                        item.DropAndTossUpwards(player.transform.position);
                    }
                }
                player.Kill();
            }
            //Create new player for them to spawn into
            player = global::GameManager.server.CreateEntity("assets/prefabs/player/player.prefab", FerryPos.transform.position, FerryPos.transform.rotation, true).ToPlayer();
            player.lifestate = global::BaseCombatEntity.LifeState.Alive;
            player.ResetLifeStateOnSpawn = false;
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
            player.transform.localRotation =rot;
            player.TransformChanged();
            player.SendNetworkUpdateImmediate();
            return new Dictionary<string, BaseNetworkable>() { { steamid, player } };
        }

        private void TeleportPlayer(BasePlayer player, Vector3 pos)
        {
            try
            {
                player.EnsureDismounted();
                if (player.HasParent()) { player.SetParent(null, true, true); }
                player.RemoveFromTriggers();
                player.SetServerFall(true);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
                player.Teleport(pos);
                player.SendEntityUpdate();
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
                        case "slot":
                            //One item read from packet
                            GiveContents(ownerid, id, condition, amount, skinid, code, int.Parse(ii.Value), CreatedEntitys);
                            break;
                    }
                }
            }
        }

        private void GiveContents(string ownerid, int id, float condition, int amount, ulong skinid, int code, int slot, Dictionary<string, BaseNetworkable> CreatedEntitys)
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
                    //BasePlayers items
                    BasePlayer bp = FoundEntity as BasePlayer;
                    if (bp != null)
                    {
                        switch (slot)
                        {
                            //Put back in players inventory
                            case 0:
                                //bp.inventory.containerWear.Insert(CreateItem(id, amount, skinid, condition, code));
                                CreateItem(id, amount, skinid, condition, code).MoveToContainer(bp.inventory.containerWear);
                                break;
                            case 1:
                                Item item = BuildWeapon(id, skinid, condition, code, null);
                                //bp.inventory.containerBelt.Insert(item); //Shows up but isnt usable.
                                item.MoveToContainer(bp.inventory.containerBelt); //Nothing at all shows up.
                                break;
                            case 2:
                                // bp.inventory.containerMain.Insert(CreateItem(id, amount, skinid, condition, code));
                                CreateItem(id, amount, skinid, condition, code).MoveToContainer(bp.inventory.containerMain);
                                break;
                        }
                        return;
                    }
                }
            }
        }

        private Item BuildWeapon(int id, ulong skin, float condition, int code, List<int> mods)
        {
            Item item = CreateItem(id, 1, skin, condition, code);
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
                    item.contents.AddItem(BuildItem(mod, 1, 0, condition,code,0).info, 1);
                }
            }
            return item;
        }

        private Item BuildItem(int itemid, int amount, ulong skin, float condition, int code, int blueprintTarget)
        {
            if (amount < 1) amount = 1;
            Item item = CreateItem(itemid, amount, skin, condition, code);
            if (blueprintTarget != 0)
                item.blueprintTarget = blueprintTarget;
            return item;
        }

        private Item CreateItem(int id, int amount, ulong skinid, float condition, int code)
        {
            //Create a item
            Item item = ItemManager.Create(ItemManager.FindItemDefinition(id), amount, skinid);
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
                            foreach (global::BaseEntity baseEntity in car.children)
                            {
                                if (baseEntity == null) continue;
                                foreach (global::BaseEntity baseEntity2 in baseEntity.children)
                                {
                                    if (baseEntity2 == null) continue;
                                    SleepingBag sleepingBagCamper;
                                    if ((sleepingBagCamper = (baseEntity2 as global::SleepingBag)) != null)
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
            if (item.info.shortname == "car.key" || item.info.shortname == "door.key")
            {
                //Add keys code data
                code = (item.instanceData.dataInt.ToString());
            }
            var itemdata = new Dictionary<string, string>
                        {
                        { "ownerid", Owner },
                        { "id", item.info.itemid.ToString() },
                        { "condition", item.condition.ToString() },
                        { "amount", item.amount.ToString() },
                        { "skinid", item.skin.ToString() },
                        { "code", code},
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
            var itemdata = new Dictionary<string, string>
                        {
                        { "steamid", baseplayer.UserIDString },
                        { "name", baseplayer.displayName.ToString() },
                        { "position", FerryPos.transform.InverseTransformPoint(baseplayer.transform.position).ToString() },
                        { "rotation", (baseplayer.transform.localRotation).ToString()},
                        { "health", baseplayer._health.ToString() },
                        { "maxhealth", baseplayer._health.ToString() },
                        { "hydration", baseplayer.metabolism.hydration.value.ToString() },
                        { "calories", baseplayer.metabolism.calories.value.ToString() }
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

        public class OpenNexusFerry : BaseEntity
        {
            //Variables
            Transform FerryPos;
            public string ServerIP = "";
            public string ServerPort = "";
            public float MoveSpeed = 10f;
            public float TurnSpeed = 1f;
            public float WaitTime = 10;
            private byte Retrys = 0;
            private global::NexusFerry.State _state;
            private global::NexusDock _targetDock;
            private TimeSince _sinceStartedWaiting;
            private bool _isTransferring;
            private List<BaseNetworkable> DockedEntitys = new List<BaseNetworkable>();

            //States it transitions between
            public enum State
            {
                Invalid,
                Arrival,
                Docking,
                Stopping,
                Waiting,
                CastingOff,
                Departure,
                Transferring
            }

            private void Awake()
            {
                //setup
                gameObject.layer = 0;
                FerryPos = this.transform;
                if (this._targetDock == null)
                {
                    this._targetDock = SingletonComponent<NexusDock>.Instance;
                    //Adjust travel distance
                    _targetDock.Departure.position = _targetDock.Docked.position + (FerryPos.rotation * Vector3.forward) * (100 + plugin.ExtendFerryDistance);
                    if (this._targetDock == null)
                    {
                        Debug.LogError("NexusFerry has no dock to go to!");
                        base.Kill(global::BaseNetworkable.DestroyMode.None);
                        return;
                    }
                }

                if (this._state == global::NexusFerry.State.Invalid)
                {
                    this._state = global::NexusFerry.State.Stopping;
                    Transform targetTransform = this.GetTargetTransform(this._state);
                    base.transform.SetPositionAndRotation(targetTransform.position, targetTransform.rotation);
                }
                //Sync servers with hub so both start at same time
                plugin.SyncServers();
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
                if (this != null && !this.IsDestroyed)
                    Destroy(this);
            }

            private void Update()
            {
                //Stops ferry doing anything if not setup/synced
                if (!plugin.setup || ServerIP == "" || ServerPort == "" || _isTransferring || !plugin.ServerSynced) { return; }
                if (this == null)
                {
                    Die();
                    return;
                }
                if (!base.isServer)
                {
                    return;
                }
                if (this._state == global::NexusFerry.State.Waiting)
                {
                    if (this._sinceStartedWaiting < this.WaitTime)
                    {
                        return;
                    }
                    SwitchToNextState();
                }
                if (MoveTowardsTarget())
                {
                    SwitchToNextState();
                }
            }

            private Transform GetTargetTransform(global::NexusFerry.State state)
            {
                switch (state)
                {
                    case global::NexusFerry.State.Arrival:
                        return this._targetDock.Arrival;
                    case global::NexusFerry.State.Docking:
                        return this._targetDock.Docking;
                    case global::NexusFerry.State.Stopping:
                    case global::NexusFerry.State.Waiting:
                        return this._targetDock.Docked;
                    case global::NexusFerry.State.CastingOff:
                        return this._targetDock.CastingOff;
                    case global::NexusFerry.State.Departure:
                        return this._targetDock.Departure;
                    default:
                        return base.transform;
                }
            }

            private void ReCheck()
            {
                //Keep checking for incoming data for next 60 secs
                if (!plugin.HasPacket)
                {
                    TransferOpenNexus(true);
                    if (Retrys <= 6)
                    {
                        Invoke(() => ReCheck(), 10f);
                        Retrys++;
                        return;
                    }
                }
                plugin.HasPacket = false;
                Retrys = 0;
                Invoke(() => Progress(), 60f);
            }

            private void Progress()
            {
                this._state = NexusFerry.State.Arrival;
                this._isTransferring = false;
            }

            public void UpdateDockedEntitys()
            {
                //Load DockedEntitys with everything paranted to Ferry
                DockedEntitys = GetFerryContents();
            }

            private void SwitchToNextState()
            {
                if (this._state == global::NexusFerry.State.Departure)
                {
                    if (!_isTransferring)
                    {
                        TransferOpenNexus();
                        Invoke(() => ReCheck(), 5f);
                    }
                    return;
                }
                this._state = GetNextState(this._state);
                if (this._state == global::NexusFerry.State.Waiting)
                {
                    this._sinceStartedWaiting = 0f;
                    return;
                }
                if (this._state == global::NexusFerry.State.Docking)
                {
                    UpdateDockedEntitys();
                    return;
                }
                if (this._state == global::NexusFerry.State.CastingOff)
                {
                    //Kick off all the entitys that already been on ferry
                    EjectEntitys();
                    return;
                }
            }

            private NexusFerry.State GetPreviousState(global::NexusFerry.State currentState)
            {
                if (currentState != global::NexusFerry.State.Invalid)
                {
                    return currentState - 1;
                }
                return global::NexusFerry.State.Invalid;
            }

            private NexusFerry.State GetNextState(global::NexusFerry.State currentState)
            {
                global::NexusFerry.State state = currentState + 1;
                if (state >= global::NexusFerry.State.Departure)
                {
                    state = global::NexusFerry.State.Departure;
                }
                return state;
            }

            public bool MoveTowardsTarget()
            {
                Transform targetTransform = this.GetTargetTransform(this._state);
                Vector3 position = targetTransform.position;
                Quaternion rotation = targetTransform.rotation;
                Vector3 position2 = base.transform.position;
                position.y = position2.y;
                Vector3 a;
                float num;
                (position - position2).ToDirectionAndMagnitude(out a, out num);
                float num2 = this.MoveSpeed * Time.deltaTime;
                float num3 = Mathf.Min(num2, num);
                Vector3 position3 = position2 + a * num3;
                Quaternion rotation2 = base.transform.rotation;
                global::NexusFerry.State previousState = GetPreviousState(this._state);
                Quaternion rotation4;
                if (previousState != global::NexusFerry.State.Invalid)
                {
                    Transform targetTransform2 = this.GetTargetTransform(previousState);
                    Vector3 position4 = targetTransform2.position;
                    Quaternion rotation3 = targetTransform2.rotation;
                    position4.y = position2.y;
                    float num4 = Vector3Ex.Distance2D(position4, position);
                    rotation4 = Quaternion.Slerp(rotation, rotation3, num / num4);
                }
                else
                {
                    rotation4 = Quaternion.Slerp(rotation2, rotation, this.TurnSpeed * Time.deltaTime);
                }
                base.transform.SetPositionAndRotation(position3, rotation4);
                return num3 < num2;
            }

            private List<BaseNetworkable> GetFerryContents()
            {
                List<BaseNetworkable> list = Pool.GetList<BaseNetworkable>();
                foreach (BaseNetworkable baseEntity in this.children)
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

            public void EjectEntitys()
            {
                //Ejects anything left on Ferry to dock.
                List<BaseNetworkable> currentcontents = GetFerryContents();
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
                        if (this._targetDock.TryFindEjectionPosition(out serverPosition, 5f))
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

            private void TransferOpenNexus(bool recheck = false)
            {
                string Datapacket = "";
                List<BaseNetworkable> list = new List<BaseNetworkable>();
                if (!recheck)
                {
                    this._state = global::NexusFerry.State.Transferring;
                    //Get all entitys on the  ferry to transfere
                    list = GetFerryContents();
                    //Check if anything to transfere
                    if (list.Count != 0)
                    {
                        //Create a packet to send to nexus hub
                        Datapacket = (plugin.CreatePacket(list, this));
                        if (plugin.UseCompression) { Datapacket = Convert.ToBase64String(Compression.Compress(Encoding.UTF8.GetBytes(Datapacket))); } //Compress Data
                    }
                    this._isTransferring = true;
                }
                Dictionary<string, string> headers = new Dictionary<string, string> { { "Content-Length", Datapacket.Length.ToString() }, { "User-Agent", plugin.OpenNexusKey }, { "TARGET", ServerIP + "-" + ServerPort }, { "FROM", plugin.thisserverip + "-" + plugin.thisserverport } };
                if (!recheck)
                {
                    headers.Add("CMD", "WRITE");
                }
                plugin.webrequest.Enqueue(plugin.OpenNexusHub, Datapacket, (code, response) =>
                {
                if (response == null || code != 200)
                {
                    plugin.Puts("OpenNexus Hub didn't respond!");
                    if (list.Count != 0)
                    {
                        foreach (BaseEntity be in list)
                        {
                            if (be is BasePlayer)
                            {
                                if (be.ToPlayer().IsConnected)
                                {
                                    be.ToPlayer().ChatMessage("OpenNexus Hub didn't respond!");
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (Datapacket.Length != 0 || response.Length != 0)
                    {
                        plugin.Puts("OpenNexus Sent " + String.Format("{0:0.##}", (double)(Datapacket.Length / 1024f)) + " Kb");
                        plugin.Puts("OpenNexus Read " + String.Format("{0:0.##}", (double)(response.Length / 1024f)) + " Kb");
                    }
                    if (list.Count != 0)
                    {
                        foreach (BaseEntity be in list)
                        {
                            if (be is BasePlayer && !be.IsNpc)
                            {
                                BasePlayer bp = be as BasePlayer;
                                if (bp.IsConnected)
                                {
                                        bp.ChatMessage("OpenNexus Switching Server in 20 sec");
                                        Invoke(() =>
                                        {
                                            plugin.AdjustConnectionScreen(bp, "OpenNexus Switching Server");
                                            bp.ClientRPCPlayer(null, bp, "StartLoading");
                                            bp.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
                                            ConsoleNetwork.SendClientCommand(bp.net.connection, "nexus.redirect", new object[]
                                            {
                                            ServerIP,
                                            ServerPort
                                            });
                                            bp.ToPlayer().Kick("OpenNexus Moving Server");
                                            bp.Kill();
                                        }, 20f);
                                        continue;
                                    }
                                }
                                be.Kill();
                            }
                        }
                        //ReadPacket from OpenNexus Hub
                        Datapacket = response;
                        if (plugin.UseCompression && plugin.IsBase64String(Datapacket)) { Datapacket = Encoding.UTF8.GetString(Compression.Uncompress(Convert.FromBase64String(Datapacket))); } //Uncompressed
                        plugin.ReadPacket(Datapacket, this);
                    }
                }, plugin, RequestMethod.POST, headers, 10f);
            }
        }
    }
}