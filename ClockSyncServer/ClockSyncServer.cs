using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ClockSyncServer
{
    class ClockSyncServer
    {
        private static List<ClientObject> clients = new List<ClientObject>();
        private static byte[] heartbeat;

        public static void Main(string[] args)
        {
            Console.WriteLine("Server running, press ctrl+c to exit.");
            UdpClient udp = new UdpClient(new IPEndPoint(IPAddress.IPv6Any, 2076));
            udp.BeginReceive(HandleReceive, udp);
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write("CST");
                    bw.Write(0);
                }
                heartbeat = ms.GetBuffer();
            }
            while (true)
            {
                Thread.Sleep(1000);
                lock (clients)
                {
                    SendToAll(udp);
                }
                //UpdateConsole();
            }
        }

        private static void SendToAll(UdpClient udp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    //Remove old clients
                    for (int i = clients.Count - 1; i >= 0; i--)
                    {
                        ClientObject client = clients[i];
                        if (DateTime.UtcNow.Ticks > (client.lastTime + 10 * TimeSpan.TicksPerSecond))
                        {
                            clients.RemoveAt(i);
                        }
                    }
                    //Create the message
                    bw.Write("CST");
                    bw.Write(2);
                    bw.Write(clients.Count);
                    for (int i = 0; i < clients.Count; i++)
                    {
                        ClientObject client = clients[i];
                        bw.Write(client.name);
                        bw.Write(client.offset);
                        bw.Write(client.latency);
                        bw.Write(client.epoch);
                        bw.Write(client.universeTime);
                        bw.Write(client.rate);
                    }
                }
                foreach (ClientObject client in clients)
                {
                    byte[] sendBytes = ms.GetBuffer();
                    try
                    {
                        udp.Send(sendBytes, sendBytes.Length, client.endpoint);
                        udp.Send(heartbeat, heartbeat.Length, client.endpoint);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Send error: " + e);
                    }
                }
            }
        }

        private static void Heartbeat(IPEndPoint endpoint)
        {
            foreach (ClientObject client in clients)
            {
                if (client.endpoint.Address == endpoint.Address && client.endpoint.Port == endpoint.Port)
                {
                    client.lastTime = DateTime.UtcNow.Ticks;
                    return;
                }
            }
        }

        private static void UpdateEndpoint(IPEndPoint endpoint, string name, long offset, long latency, long epoch, double universeTime, float rate)
        {
            foreach (ClientObject client in clients)
            {
                if (client.endpoint.Address == endpoint.Address && client.endpoint.Port == endpoint.Port)
                {
                    client.lastTime = DateTime.UtcNow.Ticks;
                    client.name = name;
                    client.offset = offset;
                    client.latency = latency;
                    client.epoch = epoch;
                    client.universeTime = universeTime;
                    client.rate = rate;
                    return;
                }
            }
            ClientObject newClient = new ClientObject();
            newClient.endpoint = endpoint;
            newClient.lastTime = DateTime.UtcNow.Ticks;
            newClient.name = name;
            newClient.offset = offset;
            newClient.latency = latency;
            newClient.epoch = epoch;
            newClient.universeTime = universeTime;
            newClient.rate = rate;
            clients.Add(newClient);
        }

        private static void HandleReceive(IAsyncResult ar)
        {
            UdpClient udp = (UdpClient)ar.AsyncState;
            IPEndPoint endpoint = null;
            SafeReceive(ar, ref endpoint);
            //UnsafeReceive(ar, ref endpoint);
            udp.BeginReceive(HandleReceive, udp);
        }

        public static void SafeReceive(IAsyncResult ar, ref IPEndPoint endpoint)
        {
            try
            {
                UnsafeReceive(ar, ref endpoint);
            }
            catch (Exception e)
            {
                if (endpoint != null)
                {
                    Console.WriteLine("Error receiving from: " + endpoint + ", type: " + e.GetType());
                }
            }
        }

        public static void UnsafeReceive(IAsyncResult ar, ref IPEndPoint endpoint)
        {
            UdpClient udp = (UdpClient)ar.AsyncState;
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
                            //Console.WriteLine("Received message: " + messageType + " from " + endpoint);
                            if (messageType == 0)
                            {
                                Heartbeat(endpoint);
                            }
                            if (messageType == 1)
                            {
                                using (MemoryStream ms2 = new MemoryStream())
                                {
                                    using (BinaryWriter bw2 = new BinaryWriter(ms2))
                                    {
                                        bw2.Write("CST");
                                        bw2.Write(1);
                                        bw2.Write(br.ReadInt64());
                                        bw2.Write(DateTime.UtcNow.Ticks);

                                    }
                                    byte[] sendTimeBytes = ms2.GetBuffer();
                                    udp.Send(sendTimeBytes, sendTimeBytes.Length, endpoint);
                                }
                            }
                            if (messageType == 2)
                            {
                                string name = br.ReadString();
                                long offset = br.ReadInt64();
                                long latency = br.ReadInt64();
                                long epoch = br.ReadInt64();
                                double universeTime = br.ReadDouble();
                                float rate = br.ReadSingle();
                                Console.WriteLine(name + ": latency: " + Math.Round(latency / (double)TimeSpan.TicksPerMillisecond, 2) + "ms, offset: " + Math.Round(offset / (double)TimeSpan.TicksPerMillisecond, 2) + "ms.");
                                lock (clients)
                                {
                                    UpdateEndpoint(endpoint, name, offset, latency, epoch, universeTime, rate);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}