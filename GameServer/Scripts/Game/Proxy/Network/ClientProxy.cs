using Net;
using PureMVC.Patterns.Proxy;
using System.Collections.Generic;
using System.Net.Sockets;

namespace Game
{
    public class ClientProxy : Proxy
    {

        public new static string NAME = typeof(ClientProxy).FullName;

        private List<TCPClientState> clients;
        private Dictionary<TcpClient, TCPClientState> clientMap;


        public ClientProxy() : base(NAME) { }

        public override void OnRegister()
        {
            base.OnRegister();
            clients = new List<TCPClientState>();
        }

        public override void OnRemove()
        {
        }

        public List<TCPClientState> Clients => clients;

        public void Add(TCPClientState state)
        {
            clients.Add(state);
        }

        public void Remove(TCPClientState state)
        {
            clients.Remove(state);
        }

        public void Set(TCPClientState state)
        {
            clients.Add(state);
        }

        public TCPClientState Get(int i)
        {
            return clients[i];

        }
        public void SetSsid(long ssid)
        {


        }

        public void Close(TCPClientState state)
        {
            state.Close();
            clients.Remove(state);
        }

    }
}
