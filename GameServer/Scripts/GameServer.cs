using System;
using Server;

namespace GameServer
{
    class GameServer
    {
        static void Main(string[] args)
        {
            TCPServer server = new TCPServer();
            server.Start();

            while(true)
            {

            }
        }

    }
}
