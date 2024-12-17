using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace drvVesy31
{
    public class ServerMode
    {
        private static readonly log4net.ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        volatile bool  _run = true; // указатель на изменяемый объект
        Connection     conn  = null;
        
        public ServerMode()
        {
        }

        public int Run(
            Dictionary<string, string> d,
            UdpClient udpServer,
            CancellationTokenSource udpCts)
        {
            logger.Info("SOCKET SERVER MODE"); 
            DateTime dtPrevious = DateTime.Now;
            _run = true;

            Socket srv = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            srv.Bind(new IPEndPoint(IPAddress.Any, int.Parse( d["clientPort"].Trim() )));
            srv.Listen(1);
            srv.ReceiveTimeout = 10;

            while (_run)
            {
                Socket soc = null;

                try
                {
                    if ((udpServer is null) && srv.Poll(1000, SelectMode.SelectRead))
                        soc = srv.Accept();
                }
                catch (Exception ex) // для удобства - пишу исключение и пробрасываю наверх, как предусмотрено первоначальной логикой
                {
                    logger.Error(ex);
                    throw;
                }

                if ((udpServer is null) && soc != null)
                {
                    logger.Info("Tcp client connected");
                    conn = new Connection();
                    try
                    {
                        bool didStart = conn.StartConnection(soc, 
                                                             d,
                                                             udpServer,
                                                             udpCts);
                    }
                    catch(Exception ex)
                    {
                        logger.Error(ex);
                        conn = null;
                    }
                }
                else
                {
                    if (DateTime.Now.Subtract( dtPrevious).TotalSeconds > 10)
                    {
                        logger.Info("Server active and idle");
                        dtPrevious = DateTime.Now;
                    }
                    Thread.Sleep(1);
                }
            }

            logger.Info("Server shutting down");
            srv.Close();
            srv = null;
            conn = null;
            
            return(0);
        }

        public void StopRequest()
        {
            if (conn != null)
                conn.StopRequest();

            _run = false;
            Thread.Sleep(1000);
        }
    }
}
