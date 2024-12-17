/*
cd C:\Windows\Microsoft.NET\Framework64\v4.0.30319
.\InstallUtil.exe D:\repos\cSharp\job\pipeComToIp\drvVesy31\bin\Release\drvVesy31.exe
.\InstallUtil.exe /u D:\repos\cSharp\job\pipeComToIp\drvVesy31\bin\Release\drvVesy31.exe
Ставим запуск службы в автомате в ручную. При установке автозапуска службы в проекте, почему-то иногда не срабатывает. 
 */
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;

namespace drvVesy31
{
    public partial class Service1 : ServiceBase
    {
        private static readonly log4net.ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private CancellationTokenSource _udpCts;    // vesy 31
        private UdpClient _udpServer;               // vesy 31
        ServerMode sm = null;
        Dictionary<string, string> dn = new Dictionary<string, string>();
        protected Thread thServiceThread = null;
        protected bool _connected = false;
        protected bool _is_shown = false;

        public Service1()
        {
            SetDefaultCulture();
            InitializeComponent();
            logger.Info("InitializeComponent()");
            dn = new Dictionary<string, string>();
            dn.Add("Vesy", ConfigurationManager.AppSettings["Vesy"]);                                       // ?
            dn.Add("clientHost", ConfigurationManager.AppSettings["ArmAddress"]);
            dn.Add("clientPort", ConfigurationManager.AppSettings["ArmPort"]);
            dn.Add("vesy31ip", ConfigurationManager.AppSettings["ControllerAddress"]);                      // для контроллера 31-х весов
            dn.Add("vesy31port", ConfigurationManager.AppSettings["ControllerPort"]);                       // для контроллера 31-х весов
            dn.Add("determiningTheEndian", ConfigurationManager.AppSettings["DeterminingTheEndian"]);       // определение порядка байтов        
        }

        public void RunAsConsole(string[] args)
        {
            OnStart(args);
            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
            OnStop();
        }

        protected override void OnStart(string[] args)
        {
            logger.Info("OnStart");
            thServiceThread = new Thread(ServiceThread);
            thServiceThread.Start();
        }

        void ServiceThread()
        {
            int retval = 0;

            logger.Info("ServiceThread start " + this.dn["clientHost"] + " " + int.Parse(this.dn["clientPort"].Trim()).ToString());
            try
            {
                sm = new ServerMode();
                retval = sm.Run(dn,
                                _udpServer,
                                _udpCts);
            }
            catch (Exception ex) 
            { 
                logger.Error("Mode start failed.." + "\r\nMS:" + ex.Message + "\r\nST:" + ex.StackTrace);
            }
            logger.Info("Service stopped");
            
        }

        protected override void OnStop()
        {
            logger.Info("OnStop");
            HandleStop();
        }

        void HandleStop()
        {
            if (sm != null)
            {
                logger.Info("Stop request set to Server Mode");
                sm.StopRequest();
            }
            sm = null;

            if (_udpCts != null) 
            {
                _udpCts.Cancel();
                _udpCts = null;
            }

            if (_udpServer != null) 
            {
                _udpServer.Close();
                _udpServer = null;
            }
        }

        public static void SetDefaultCulture()
        {
            var cultureInfo = new CultureInfo("en-GB")
            {
                DateTimeFormat = 
                { 
                    ShortDatePattern = "dd.MM.yyyy", 
                    LongDatePattern = "dd.MM.yyyy HH:mm:ss", 
                    LongTimePattern = "HH:mm:ss",
                    ShortTimePattern = "HH:mm",
                    DateSeparator = ".",
                    TimeSeparator = ":"
                },
                NumberFormat = 
                { 
                    NumberDecimalSeparator = "."
                }
            };

            Thread.CurrentThread.CurrentCulture = cultureInfo;
            Thread.CurrentThread.CurrentUICulture = cultureInfo;
            CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
            CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

            Type type = typeof(CultureInfo);
            type.InvokeMember("s_userDefaultCulture",
                                BindingFlags.SetField | BindingFlags.NonPublic | BindingFlags.Static,
                                null,
                                cultureInfo,
                                new object[] { cultureInfo });

            type.InvokeMember("s_userDefaultUICulture",
                                BindingFlags.SetField | BindingFlags.NonPublic | BindingFlags.Static,
                                null,
                                cultureInfo,
                                new object[] { cultureInfo });
        }

    }

}
