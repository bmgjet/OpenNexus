using Facepunch;
using Oxide.Core.Libraries;
using ProtoBuf;
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
        public string ServerIP = "";
        public string ServerPort = "";
        public string OpenNexusHub = "";
        public string OpenNexusKey = "";

        private void OnServerInitialized()
        {
            plugin = this;
            //Create variables
            Quaternion rotation = Quaternion.Euler(Vector3.zero);
            Vector3 position = Vector3.zero;
            //Scan map prefabs
            foreach (PrefabData prefabdata in World.Serialization.world.prefabs)
            {
                //Only target Nexus Ferry
                if (prefabdata.id == 2508295857)
                {
                    rotation = Quaternion.Euler(new Vector3(prefabdata.rotation.x, prefabdata.rotation.y, prefabdata.rotation.z));
                    position = new Vector3(prefabdata.position.x, prefabdata.position.y, prefabdata.position.z);
                    NexusFerry ferry = (NexusFerry)GameManager.server.CreateEntity(StringPool.Get(prefabdata.id), prefabdata.position, prefabdata.rotation) as NexusFerry;
                    if (ferry == null) return;
                    OpenNexusFerry OpenFerry = ferry.gameObject.AddComponent<OpenNexusFerry>();
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

        void OnPlayerConnected(Network.Message packet)
        {
            var player = packet.Player();
            //Contact OpenNexus Hub and get any contents for this player.
            //To Do
        }

        private void Unload()
        {
            //Remove static reference
            plugin = null;
            //Remove any NexusFerrys
            foreach (var basenetworkable in BaseNetworkable.serverEntities) { if (basenetworkable.gameObject.name == "NexusFerry" && !basenetworkable.IsDestroyed) { basenetworkable.Kill(); } }
        }


        public class OpenNexusFerry : BaseEntity
        {
            public float MoveSpeed = 10f;
            public float TurnSpeed = 1f;
            public float WaitTime = 10f;
            private global::NexusFerry.State _state;
            private NexusDock _targetDock;
            private bool _isTransferring;
            private TimeSince _sinceStartedWaiting;
            private TimeSince _sinceLastTransferAttempt;

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
                gameObject.name = "NexusFerry";
                if (this._targetDock == null)
                {
                    this._targetDock = SingletonComponent<NexusDock>.Instance;
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

            private void SwitchToNextState()
            {
                if (this._state == global::NexusFerry.State.Departure)
                {
                    if (!this._isTransferring)
                    {
                        this.TransferToNextZone();
                    }
                    return;
                }
                this._state = GetNextState(this._state);
                if (this._state == global::NexusFerry.State.Waiting)
                {
                    this._isTransferring = false;
                    this._sinceStartedWaiting = 0f;
                }
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

            private string CreatePacket(List<BaseNetworkable> Transfere)
            {
                string dat = "";
                foreach (BaseNetworkable entity in Transfere)
                {
                    BasePlayer baseplayer = entity as BasePlayer;
                    if (baseplayer?.inventory != null)
                    {
                        var data = new Dictionary<string, object>();
                        var itemlist = new List<Dictionary<string, object>>();
                        var playerdata = new Dictionary<string, object>
                        {
                        { "ownerid", baseplayer.UserIDString },
                        { "health", baseplayer._health },
                        { "hydration", baseplayer.metabolism.hydration.value },
                        { "calories", baseplayer.metabolism.calories.value }
                        };
                        itemlist.Add(playerdata);
                        data.Add("player", itemlist);
                        dat = Newtonsoft.Json.JsonConvert.SerializeObject(data);
                        data.Clear();
                        itemlist.Clear();

                        foreach (var item in baseplayer.inventory.AllItems())
                        {
                             var itemdata = new Dictionary<string, object>
                        {
                        { "condition", item.condition },
                        { "id", item.info.itemid },
                        { "amount", item.amount },
                        { "skinid", item.skin },
                        { "ownerid", baseplayer.UserIDString },
                        };
                            itemlist.Add(itemdata);
                        }
                        data.Add("playeritems", itemlist);
                        dat += Newtonsoft.Json.JsonConvert.SerializeObject(data);
                    }
                    MiniCopter helicopter = entity as MiniCopter;
                    if(helicopter != null)
                    {
                        var data = new Dictionary<string, object>();
                        var itemlist = new List<Dictionary<string, object>>();
                        var itemdata = new Dictionary<string, object>
                        {
                        { "ownerid", helicopter.OwnerID.ToString() },
                        { "prefabid", helicopter.prefabID.ToString() },
                        { "health", helicopter._health.ToString() },
                        { "fuel", helicopter.GetFuelSystem().GetFuelAmount().ToString() }
                        };
                        itemlist.Add(itemdata);
                        data.Add("helicopter", itemlist);
                        dat += Newtonsoft.Json.JsonConvert.SerializeObject(data);
                    }

                    ModularCar car = entity as ModularCar;
                    if (car != null)
                    {
                        var data = new Dictionary<string, object>();
                        var itemlist = new List<Dictionary<string, object>>();
                        var itemdata = new Dictionary<string, object>
                        {
                        { "ownerid", car.OwnerID.ToString() },
                        { "prefabid", car.prefabID.ToString() },
                        { "health", car._health.ToString() },
                        { "fuel", car.GetFuelSystem().GetFuelAmount().ToString() }
                        };
                        itemlist.Add(itemdata);
                        data.Add("modularcar", itemlist);
                        dat += Newtonsoft.Json.JsonConvert.SerializeObject(data);
                    }
                }
                plugin.Puts(dat);
                return dat;
            }

            public static string Base64Encode(string plainText)
            {
                var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
                return System.Convert.ToBase64String(plainTextBytes);
            }

            private void TransferToNextZone()
            {
                if (!this._isTransferring && this._sinceLastTransferAttempt >= 5f)
                {
                    this._isTransferring = true;
                    this._state = global::NexusFerry.State.Transferring;
                    //Get all entitys on the  ferry to transfere
                    List<BaseNetworkable> list = GetFerryContents();
                    //Check if anything to transfere
                    if (list.Count != 0)
                    {
                        //Create a packet to send to nexus hub
                        string Datapacket = (CreatePacket(list));
                        Dictionary<string, string> headers = new Dictionary<string, string> { { "Content-Length", Datapacket.Length.ToString() }, { "User-Agent", plugin.OpenNexusKey } };
                        plugin.webrequest.Enqueue(plugin.OpenNexusHub, Datapacket, (code, response) =>
                        {
                            if (response == null || code != 200)
                            {
                                plugin.Puts("Nexus Packet Failed ");
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
                            else
                            {
                                plugin.Puts("Nexus Packet Transfered");
                                foreach (BaseEntity be in list)
                                {
                                    if (be is BasePlayer)
                                    {
                                        if (be.ToPlayer().IsConnected)
                                        {
                                            be.ToPlayer().ChatMessage("Switch Server");
                                            ConsoleNetwork.SendClientCommand(be.net.connection, "nexus.redirect", new object[]
                                            {
                                                plugin.ServerIP,
                                                plugin.ServerPort
                                            });
                                            be.ToPlayer().Kick("Redirecting to another zone...");
                                        }
                                    }
                                }
                            }
                        }, plugin, RequestMethod.POST, headers, 60f);
                    }
                    this._state = NexusFerry.State.Arrival;
                }
            }
        }
    }
}