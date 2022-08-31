using System;
using TCPServer;

namespace GameServer
{
    class GameServer
    {
        static void Main(string[] args)
        {
            TCPServer.TCPServer server = new TCPServer.TCPServer();
            server.Start();

            while(true)
            {

            }
        }

    }
}
