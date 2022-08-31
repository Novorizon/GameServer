using System;
using System.Net.Sockets;

namespace Net
{
    public class TCPClientState
    {
        /// 与客户端相关的TcpClient
        public TcpClient TcpClient { get; private set; }

        /// 获取缓冲区
        public byte[] Buffer { get; private set; }

        /// 获取网络流
        public NetworkStream NetworkStream => TcpClient.GetStream();

        public TCPClientState(TcpClient tcpClient, byte[] buffer)
        {
            if (tcpClient == null)
                throw new ArgumentNullException("tcpClient");
            if (buffer == null)
                throw new ArgumentNullException("buffer");

            this.TcpClient = tcpClient;
            this.Buffer = buffer;
        }

        /// <summary>
        /// 关闭
        /// </summary>
        public void Close()
        {
            //关闭数据的接受和发送
            TcpClient.Close();
            Buffer = null;
        }

    }
}