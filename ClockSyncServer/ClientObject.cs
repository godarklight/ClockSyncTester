using System;
using System.Net;
namespace ClockSyncServer
{
    public class ClientObject
    {
        public IPEndPoint endpoint;
        public string name;
        //Time the server last saw the client
        public long lastTime;
        //Tick offset to server
        public long offset;
        //Server time the message was sent
        public long latency;
        //Server time the message was sent
        public long epoch;
        //Time of the universe at epoch
        public double universeTime;
        //Subspace speed (if applicable)
        public float rate = 1;
    }
}