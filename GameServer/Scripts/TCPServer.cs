using System;
using PureMVC.Interfaces;
using PureMVC.Patterns.Facade;
using Game;
using Net;

namespace Server
{
    public class TCPServer
    {
        public void Start()
        {
            Facade.RegisterProxy(new HandlerProxy());
            Facade.RegisterProxy(new ClientProxy());
            Facade.RegisterProxy(new MessageProxy());
            Facade.RegisterProxy(new NetProxy());

            NetProxy netProxy = Facade.RetrieveProxy(NetProxy.NAME) as NetProxy;

            netProxy.StartServer();
            System.Timers.Timer timer = new System.Timers.Timer();
            timer.Elapsed += new System.Timers.ElapsedEventHandler(Update);//到达时间的时候执行事件；

        }

        public void Update(object sender, EventArgs e)
        {
            System.Timers.Timer timer = sender as System.Timers.Timer;
        }


        protected IFacade Facade => PureMVC.Patterns.Facade.Facade.GetInstance(() => new Facade());

        public void SendNotification(string notificationName, object body = null, string type = null)
        {
            Facade.SendNotification(notificationName, body, type);
        }
    }
}