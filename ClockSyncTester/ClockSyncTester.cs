using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using ClockSyncServer;
using System.Reflection;

namespace ClockSyncTester
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    class ClockSyncTester : MonoBehaviour
    {
        private Dictionary<string, ClientObject> clients = new Dictionary<string, ClientObject>();
        private UdpClient udp4;
        private UdpClient udp6;
        private byte[] heartbeat;
        private long lastSendTime;
        private long lastUpdateTime;
        private long ouroffset;
        private long ourlatency;
        private FieldInfo dmpClient;
        private FieldInfo dmpGame;
        private FieldInfo dmpTimeSyncer;
        private MethodInfo dmpCurrentSubspace;
        private MethodInfo dmpGetSubspace;
        private FieldInfo dmpRateField;
        private bool isDMP = false;

        public void Awake()
        {
            try
            {
                foreach (Assembly ass in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (ass.GetName().FullName.StartsWith("DarkMultiPlayer,", StringComparison.Ordinal))
                    {
                        dmpClient = ass.GetType("DarkMultiPlayer.Client").GetField("dmpClient");
                        dmpGame = ass.GetType("DarkMultiPlayer.Client").GetField("dmpGame");
                        dmpTimeSyncer = ass.GetType("DarkMultiPlayer.DMPGame").GetField("timeSyncer");
                        dmpCurrentSubspace = ass.GetType("DarkMultiPlayer.TimeSyncer").GetProperty("currentSubspace").GetGetMethod();
                        dmpGetSubspace = ass.GetType("DarkMultiPlayer.TimeSyncer").GetMethod("GetSubspace");
                    }
                    if (ass.GetName().FullName.StartsWith("DarkMultiPlayer-Common,", StringComparison.Ordinal))
                    {
                        dmpRateField = ass.GetType("DarkMultiPlayerCommon.Subspace").GetField("subspaceSpeed");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("ClockSyncTester: Failed to get DMP!");
                Debug.LogException(e);
            }
            if (dmpClient != null && dmpGetSubspace != null && dmpCurrentSubspace != null && dmpRateField != null)
            {
                Debug.Log("ClockSyncTester: Found DMP!");
                isDMP = true;
            }
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write("CST");
                    bw.Write(0);
                }
                heartbeat = ms.GetBuffer();
            }
            IPAddress[] addrs = Dns.GetHostAddresses("godarklight.info.tm");
            IPAddress selectedAddressv4 = null;
            IPAddress selectedAddressv6 = null;
            foreach (IPAddress addr in addrs)
            {
                if (addr.AddressFamily == AddressFamily.InterNetwork)
                {
                    selectedAddressv4 = addr;
                }
                if (addr.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    selectedAddressv6 = addr;
                }
            }
            if (selectedAddressv4 != null)
            {
                udp4 = new UdpClient(AddressFamily.InterNetwork);
                udp4.Connect(new IPEndPoint(selectedAddressv4, 2076));
                udp4.BeginReceive(HandleReceive, udp4);
                Debug.Log("ClockSyncTester: V4 found: " + selectedAddressv4);
            }
            if (selectedAddressv6 != null)
            {
                udp6 = new UdpClient(AddressFamily.InterNetworkV6);
                udp6.Connect(new IPEndPoint(selectedAddressv6, 2076));
                udp6.BeginReceive(HandleReceive, udp6);
                Debug.Log("ClockSyncTester: V6 found: " + selectedAddressv6);
            }
            DontDestroyOnLoad(this);
            Debug.Log("ClockSyncTester Loaded!");
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (DateTime.UtcNow.Ticks > (lastSendTime + 1 * TimeSpan.TicksPerSecond))
                {
                    lastSendTime = DateTime.UtcNow.Ticks;
                    Debug.Log("ClockSyncTester: Sending to server");
                    SendTime();
                    Send();
                }
                string smText = "";
                if (DateTime.UtcNow.Ticks > (lastUpdateTime + .1 * TimeSpan.TicksPerSecond))
                {
                    lastUpdateTime = DateTime.UtcNow.Ticks;
                    foreach (ClientObject client in clients.Values)
                    {
                        long serverClock = DateTime.UtcNow.Ticks + ouroffset;
                        long timeDiff = serverClock - client.epoch;
                        double timeDiffD = timeDiff / (double)TimeSpan.TicksPerSecond;
                        double universeTimeDiff = Planetarium.GetUniversalTime() - (client.universeTime + timeDiffD * client.rate);
                        smText = client.name + " UT: " + System.Math.Round(universeTimeDiff * 1000) + "ms, lag: " + System.Math.Round(client.latency / (double)TimeSpan.TicksPerMillisecond) + "ms.\n";
                    }
                }
            }
        }

        private void SendTime()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write("CST");
                    bw.Write(1);
                    bw.Write(DateTime.UtcNow.Ticks);
                }
                byte[] timeBytes = ms.GetBuffer();
                if (udp4 != null)
                {
                    udp4.Send(timeBytes, timeBytes.Length);
                }
                if (udp6 != null)
                {
                    udp6.Send(timeBytes, timeBytes.Length);
                }
            }
        }

        private void Send()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write("CST");
                    bw.Write(2);
                    string sendname = "Unknown";
                    if (FlightGlobals.ready && FlightGlobals.fetch.activeVessel != null)
                    {
                        sendname = FlightGlobals.fetch.activeVessel.GetDisplayName();
                    }
                    bw.Write(sendname);
                    bw.Write(ouroffset);
                    bw.Write(ourlatency);
                    long serverClock = DateTime.UtcNow.Ticks + ouroffset;
                    bw.Write(serverClock);
                    bw.Write(Planetarium.GetUniversalTime());
                    bw.Write(GetRate());
                }
                byte[] sendBytes = ms.GetBuffer();
                try
                {
                    if (udp4 != null)
                    {
                        udp4.Send(sendBytes, sendBytes.Length);
                    }
                    if (udp6 != null)
                    {
                        udp6.Send(sendBytes, sendBytes.Length);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Send error: " + e);
                }
            }
        }


        private void UpdateEndpoint(string endpointname, long offset, long latency, long epoch, double universeTime, float rate)
        {
            ClientObject client = null;
            if (clients.ContainsKey(name))
            {
                client = clients[name];
            }
            else
            {
                client = new ClientObject();
                clients.Add(name, client);
            }
            client.lastTime = DateTime.UtcNow.Ticks;
            client.name = endpointname;
            client.offset = offset;
            client.latency = latency;
            client.epoch = epoch;
            client.universeTime = universeTime;
            client.rate = rate;
        }

        private void HandleReceive(IAsyncResult ar)
        {
            UdpClient udp = (UdpClient)ar.AsyncState;
            IPEndPoint endpoint = null;
            try
            {
                byte[] receive = udp.EndReceive(ar, ref endpoint);
                if (receive.Length >= 3)
                {
                    using (MemoryStream ms = new MemoryStream(receive))
                    {
                        using (BinaryReader br = new BinaryReader(ms))
                        {
                            string header = br.ReadString();
                            if (header == "CST")
                            {
                                int messageType = br.ReadInt32();
                                if (messageType == 1)
                                {
                                    long sendTime = br.ReadInt64();
                                    long serverTime = br.ReadInt64();
                                    long receiveTime = DateTime.UtcNow.Ticks;
                                    ourlatency = receiveTime - sendTime;
                                    //Server clock + ouroffset = ourtime.
                                    ouroffset = serverTime - (sendTime + ourlatency / 2);
                                }
                                if (messageType == 2)
                                {
                                    int clientCount = br.ReadInt32();
                                    for (int i = 0; i < clientCount; i++)
                                    {
                                        string endpointname = br.ReadString();
                                        long offset = br.ReadInt64();
                                        long latency = br.ReadInt64();
                                        long epoch = br.ReadInt64();
                                        double universeTime = br.ReadDouble();
                                        float rate = br.ReadSingle();
                                        UpdateEndpoint(endpointname, offset, latency, epoch, universeTime, rate);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error:" + e);
            }
            udp.BeginReceive(HandleReceive, udp);
        }

        private float GetRate()
        {
            if (isDMP)
            {
                object dmpClientObject = dmpClient.GetValue(null);
                if (dmpClientObject == null)
                {
                    Debug.Log("ClockSyncTester: DMP not found");
                    return 1f;
                }
                object dmpGameObject = dmpGame.GetValue(dmpClientObject);
                if (dmpClientObject == null)
                {
                    Debug.Log("ClockSyncTester: DMP not running");
                    return 1f;
                }
                object dmpTimeSyncerObject = dmpTimeSyncer.GetValue(dmpGameObject);
                if (dmpTimeSyncerObject == null)
                {
                    Debug.Log("ClockSyncTester: TimeSyncer not found");
                    return 1f;
                }
                int subspaceID = (int)dmpCurrentSubspace.Invoke(dmpTimeSyncerObject, null);
                if (subspaceID == -1)
                {
                    Debug.Log("ClockSyncTester: Time not locked to subspace");
                    return 1f;
                }
                object subspaceObject = dmpGetSubspace.Invoke(dmpTimeSyncerObject, new object[] {subspaceID});
                if (subspaceObject == null)
                {
                    Debug.Log("ClockSyncTester: Synced to non existant subspace");
                    return 1f;
                }
                return (float)dmpRateField.GetValue(subspaceObject);
            }
            return 1f;
        }
    }
}