using Facepunch;
using Oxide.Core.Libraries;
using ProtoBuf;
using System;
using System.Collections.Generic;
using UnityEngine;
using Time = UnityEngine.Time;

namespace Oxide.Plugins
{
    [Info("OpenNexus", "bmgjet", "1.0.0")]
    [Description("Nexus system created by bmgjet")]
    public class OpenNexus : RustPlugin
    {
        public static OpenNexus plugin;

        public string thisserverip = "203.86.200.62";
        public string thisserverport = "28070";


        //Address of opennexus php script
        public string OpenNexusHub = "http://localhost:9000/";
        //Key to limit access to only your server
        public string OpenNexusKey = "bmgjet123";
        //Extend how far the ferry goes before it triggers transistion
        public int ExtendFerryDistance = 0;

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

        private bool setup;
        private void OnServerInitialized()
        {
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
                        Puts("Dock not setup properly");
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
            //Remove static reference
            plugin = null;
            //Remove any NexusFerrys
            foreach (var basenetworkable in BaseNetworkable.serverEntities) { if (basenetworkable.prefabID == 2508295857 && !basenetworkable.IsDestroyed) { basenetworkable.Kill(); } }
        }

        public Vector3 StringToVector3(string sVector)
        {
            if (sVector.StartsWith("(") && sVector.EndsWith(")")) { sVector = sVector.Substring(1, sVector.Length - 2); }
            string[] sArray = sVector.Split(',');
            Vector3 result = new Vector3(float.Parse(sArray[0]), float.Parse(sArray[1]), float.Parse(sArray[2]));
            return result;
        }

        public void ReadPacket(string packet, Transform FerryPos)
        {
            if (packet.Contains("<OpenNexus>"))
            {
                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, List<Dictionary<string, string>>>>(packet.Replace("<OpenNexus>", ""));
                foreach (KeyValuePair<string, List<Dictionary<string, string>>> packets in data)
                {
                    plugin.Puts(packets.Key);
                    if (packets.Key.Contains("MiniCopter"))
                    {
                        MiniCopter minicopter = GameManager.server.CreateEntity("assets/content/vehicles/minicopter/minicopter.entity.prefab") as MiniCopter;
                        if (minicopter == null) continue;
                        minicopter.Spawn();
                        foreach (Dictionary<string, string> i in packets.Value)
                        {
                            foreach (KeyValuePair<string, string> ii in i)
                            {
                                plugin.Puts(ii.Key + " " + ii.Value);
                                switch (ii.Key)
                                {
                                    case "rotation":
                                        minicopter.transform.rotation = FerryPos.rotation * Quaternion.Euler(plugin.StringToVector3(ii.Value));
                                        break;
                                    case "position":
                                        minicopter.transform.position = FerryPos.position + plugin.StringToVector3(ii.Value);
                                        break;
                                    case "health":
                                        minicopter.health = float.Parse(ii.Value);
                                        break;
                                    case "fuel":
                                        if (ii.Value != "0")
                                        {
                                            var fuelContainer = minicopter?.GetFuelSystem()?.GetFuelContainer()?.inventory;
                                            if (fuelContainer != null)
                                            {
                                                var fuel = ItemManager.CreateByItemID(-946369541, int.Parse(ii.Value));
                                                if (!fuel.MoveToContainer(fuelContainer))
                                                {
                                                    fuel.Remove();
                                                }
                                            }
                                        }
                                        break;
                                    case "ownerid":
                                        minicopter.OwnerID = ulong.Parse(ii.Value);
                                        break;
                                }
                            }
                        }
                    }
                    if (packets.Key.Contains("ModularCar"))
                    {

                    }
                }
            }
        }


        public string CreatePacket(List<BaseNetworkable> Transfere, Transform FerryPos, string ServerIP, string ServerPort, string dat = "")
        {
            var data = new Dictionary<string, List<Dictionary<string, string>>>();
            foreach (BaseNetworkable entity in Transfere)
            {
                BasePlayer baseplayer = entity as BasePlayer;
                if (baseplayer?.inventory != null)
                {
                    var itemlist = new List<Dictionary<string, string>>();
                    var playerdata = new Dictionary<string, string>
                        {
                        { "name", baseplayer.displayName.ToString() },
                        { "position", (baseplayer.transform.position - FerryPos.position).ToString() },
                        { "rotation", (baseplayer.transform.rotation.eulerAngles - FerryPos.rotation.eulerAngles).ToString()},
                        { "health", baseplayer._health.ToString() },
                        { "hydration", baseplayer.metabolism.hydration.value.ToString() },
                        { "calories", baseplayer.metabolism.calories.value.ToString() }
                        };
                    itemlist.Add(playerdata);
                    data.Add("BasePlayer[" + baseplayer.UserIDString + "]", itemlist);
                    itemlist.Clear();

                    foreach (var item in baseplayer.inventory.AllItems())
                    {
                        itemlist.Add(Contents(item, baseplayer.UserIDString));
                    }
                    data.Add("Inventory[" + baseplayer.UserIDString + "]", itemlist);
                }
                MiniCopter helicopter = entity as MiniCopter;
                if (helicopter != null)
                {
                    var itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(baseVechicle(helicopter,FerryPos));
                    data.Add("MiniCopter[" + helicopter.net.ID + "]", itemlist);
                }

                ModularCar car = entity as ModularCar;
                if (car != null)
                {
                    var itemlist = new List<Dictionary<string, string>>();
                    itemlist.Add(baseVechicle(car, FerryPos));
                     data.Add("ModularCar[" + car.net.ID + "]", itemlist);
                    itemlist.Clear();
                    foreach (var moduleEntity in car.AttachedModuleEntities)
                    {
                        var vehicleModuleEngine = moduleEntity as VehicleModuleEngine;
                        var vehicleModuleStorage = moduleEntity as VehicleModuleStorage;
                        var vehicleModuleCamper = moduleEntity as VehicleModuleCamper;
                        if (vehicleModuleEngine != null)
                        {
                            var engineInventory = vehicleModuleEngine.GetContainer()?.inventory;
                            if (engineInventory != null)
                            {
                                foreach (Item item in engineInventory.itemList)
                                {
                                    itemlist.Add(Contents(item, vehicleModuleEngine.net.ID.ToString()));
                                    item.Remove();
                                }
                                data.Add("ModularCarEngine[" + vehicleModuleEngine.net.ID.ToString() + "]", itemlist);
                            }
                        }
                        if (vehicleModuleStorage != null)
                        {
                            var storageInventory = vehicleModuleStorage.GetContainer()?.inventory;
                            if (storageInventory != null)
                            {
                                foreach (Item item in storageInventory.itemList)
                                {
                                    itemlist.Add(Contents(item, vehicleModuleStorage.net.ID.ToString()));
                                    item.Remove();
                                }
                                data.Add("ModularCarStorage[" + vehicleModuleStorage.net.ID.ToString() + "]", itemlist);
                            }
                        }
                        if (vehicleModuleCamper != null)
                        {
                            var camperInventory = vehicleModuleCamper.GetContainer()?.inventory;
                            if (camperInventory != null)
                            {
                                foreach (Item item in camperInventory.itemList)
                                {
                                    itemlist.Add(Contents(item, vehicleModuleCamper.net.ID.ToString()));
                                    item.Remove();
                                }
                                data.Add("ModularCarCamper[" + vehicleModuleCamper.net.ID.ToString() + "]", itemlist);
                            }
                        }
                    }
                }
            }
            dat += "<OpenNexus>" + Newtonsoft.Json.JsonConvert.SerializeObject(data);
            plugin.Puts(dat);
            return dat;
        }

        public Dictionary<string,string> Contents(Item item, string Owner)
        {
            var itemdata = new Dictionary<string, string>
                        {
                        { "condition", item.condition.ToString() },
                        { "id", item.info.itemid.ToString() },
                        { "amount", item.amount.ToString() },
                        { "skinid", item.skin.ToString() },
                        { "ownerid", Owner },
                        };
            return itemdata;
        }

        public Dictionary<string, string> baseVechicle(BaseVehicle bv, Transform FerryPos)
        {
            var itemdata = new Dictionary<string, string>
                        {
                        { "ownerid", bv.OwnerID.ToString() },
                        { "position", (bv.transform.position - FerryPos.position).ToString() },
                        { "rotation", (bv.transform.rotation.eulerAngles - FerryPos.rotation.eulerAngles).ToString()},
                        { "health", bv._health.ToString() },
                        { "fuel", bv.GetFuelSystem().GetFuelAmount().ToString() }
                        };
            return itemdata;
        }

        public class OpenNexusFerry : BaseEntity
        {
            Transform FerryPos;
            public string ServerIP = "";
            public string ServerPort = "";
            public float MoveSpeed = 10f;
            public float TurnSpeed = 1f;
            public float WaitTime = 1f;
            private global::NexusFerry.State _state;
            private global::NexusDock _targetDock;
            private TimeSince _sinceStartedWaiting;
            private bool _isTransferring;
            private List<BaseNetworkable> DockedEntitys = new List<BaseNetworkable>();

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
                gameObject.layer = 0;
                FerryPos = this.transform;
                if (this._targetDock == null)
                {
                    this._targetDock = SingletonComponent<NexusDock>.Instance;
                    //Adjust travel distance
                    _targetDock.Departure.position = _targetDock.Departure.position + (FerryPos.rotation * Vector3.forward) * plugin.ExtendFerryDistance;
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
                //Stops ferry doing anything if not setup
                if (!plugin.setup || ServerIP == "" || ServerPort == "" || _isTransferring) { return; }
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
                if (this.MoveTowardsTarget())
                {
                    this.SwitchToNextState();
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

            private void SwitchToNextState()
            {
                if (this._state == global::NexusFerry.State.Departure)
                {
                    if (!_isTransferring)
                    {
                        //Send/get this ferrys items to/from hub
                        TransferOpenNexus();
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
                    DockedEntitys = GetFerryContents();
                    return;
                }
                if (this._state == global::NexusFerry.State.CastingOff)
                {
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
          

            private void EjectEntitys()
            {
                List<BaseNetworkable> currentcontents = GetFerryContents();
                if (currentcontents == null || currentcontents.Count == 0 || DockedEntitys == null || DockedEntitys.Count == 0)
                {
                    return;
                }
                foreach (BaseEntity entity in GetFerryContents().ToArray())
                {
                    if (entity != null && DockedEntitys.Contains(entity))
                    {
                        Vector3 serverPosition;
                        if (this._targetDock.TryFindEjectionPosition(out serverPosition, 5f))
                        {
                            entity.SetParent(null, false, false);
                            entity.ServerPosition = serverPosition;
                            entity.SendNetworkUpdateImmediate(false);
                            continue;
                        }
                    }
                }
                DockedEntitys.Clear();
            }

            private void TransferOpenNexus()
            {
                this._state = global::NexusFerry.State.Transferring;
                //Get all entitys on the  ferry to transfere
                List<BaseNetworkable> list = GetFerryContents();
                //Check if anything to transfere
                string Datapacket = "";
                if (list.Count != 0)
                {
                    //Create a packet to send to nexus hub
                    Datapacket = (plugin.CreatePacket(list,FerryPos,ServerIP,ServerPort));
                }
                this._isTransferring = true;
                Dictionary<string, string> headers = new Dictionary<string, string> { { "Content-Length", Datapacket.Length.ToString() }, { "User-Agent", plugin.OpenNexusKey }, { "Data-For", plugin.thisserverip + ":"+ plugin.thisserverport }, { "Data-Form", ServerIP + ":" + ServerPort } };
                plugin.webrequest.Enqueue(plugin.OpenNexusHub, Datapacket, (code, response) =>
                {
                    if (response == null || code != 200)
                    {
                        this._state = NexusFerry.State.Arrival;
                        this._isTransferring = false;
                        plugin.Puts("Nexus Packet Failed ");

                        if (list.Count != 0)
                        {
                            foreach (BaseEntity be in list)
                            {
                                if (be is BasePlayer)
                                {
                                    if (be.ToPlayer().IsConnected)
                                    {
                                        be.ToPlayer().ChatMessage("Nexus Hub did not respond!");
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        plugin.Puts("Nexus Packet Transfered");
                        if (list.Count != 0)
                        {
                            foreach (BaseEntity be in list)
                            {
                                if (be is BasePlayer)
                                {
                                    if (be.ToPlayer().IsConnected)
                                    {
                                        be.ToPlayer().ChatMessage("Switch Server");
                                        //ConsoleNetwork.SendClientCommand(be.net.connection, "nexus.redirect", new object[]
                                        //{
                                        //    ServerIP,
                                        //    ServerPort
                                        //});
                                        //be.ToPlayer().Kick("Redirecting to another zone...");
                                    }
                                }
                                be.Kill();
                            }
                        }
                        plugin.ReadPacket(response,FerryPos);
                        this._state = NexusFerry.State.Arrival;
                        this._isTransferring = false;
                    }
                }, plugin, RequestMethod.POST, headers, 30f);
            }
        }
    }
}