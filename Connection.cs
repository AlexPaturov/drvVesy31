using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace drvVesy31
{
    public class Connection
    {
        public Socket ARMsocket;
        private bool _isfree = true;
        private Dictionary<string, string> _d;
        private bool _keepOpen = true;
        private static readonly log4net.ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private CancellationTokenSource _udpCts;
        private UdpClient _udpServer;
        IPEndPoint _epContr31;

        public Connection()
        {
        }

        public Connection(
            Socket soc,
            Dictionary<string, string> d,
            UdpClient udpServer,
            CancellationTokenSource udpCts)
        {
            StartConnection(soc, 
                            d,
                            udpServer,
                            udpCts);
        }


        public bool IsFree() => _isfree;

        public void StopRequest()
        {
            _keepOpen = false;
        }

        public bool StartConnection(
          Socket soc,
          Dictionary<string, string> d,
          UdpClient udpServer,
          CancellationTokenSource udpCts)
        {
            ARMsocket = soc;
            _d = d;
            _udpServer = udpServer;
            _udpCts = udpCts;
            _isfree = false;
            _epContr31 = new IPEndPoint(IPAddress.Parse(_d["vesy31ip"]), Int32.Parse(_d["vesy31port"]));

            if (_udpServer is null)
            {
                logger.Info("Trying to connect to weighter ARM ");
                try
                {
                    if (_udpCts is null)
                        _udpCts = new CancellationTokenSource();
                    _udpServer = new UdpClient(Int32.Parse(_d["vesy31port"]));
                }
                catch (Exception ex)
                {
                    logger.Error(ex);

                    if (ARMsocket.Connected)
                    {
                        // ПРОВЕРИТЬ ДОСТУПНОСТЬ контроллера  - ЕСЛИ НЕ ДОСТУПЕН - СФОРМИРОВАТЬ СООБЩЕНИЕ
                        ARMsocket.Close();
                    }
                    throw;
                }
            }
            new Thread(new ThreadStart(Tranceiver)).Start();
            return true;
        }

        private async void Tranceiver()
        {
            byte[] buffer = new byte[8192];
            _keepOpen = true;
            string cliCmd = string.Empty;

            while (_keepOpen)
            {
                bool transferOccured = false; // флаг свершения передачи данных клиент <-> контроллер

                int colByteARM = LimitTo(ARMsocket.Available, 8192);
                if (colByteARM > 0) 
                {
                    ARMsocket.Receive(buffer, colByteARM, SocketFlags.None);
                    byte[] ARMbyteArr = new byte[colByteARM];
                    #region Запись в лог "строка запроса прикладного ПО драйверу"
                    Buffer.BlockCopy(buffer, 0, ARMbyteArr, 0, colByteARM);
                    logger.Info("ARM query string: " + Encoding.GetEncoding(1251).GetString(ARMbyteArr));   // строка запроса прикладного ПО драйверу
                    #endregion

                    byte[] controllerCommand = DecodeClientRequestToControllerCommand(buffer, colByteARM);  // Верну null если команда не корректная, иначе - команду для контроллера
                    if (controllerCommand != null)                                                          // команда корректна -> отправляем устройству
                    {
                        try
                        {
                            #region Если команда отлична от "Получить вес", то отправляю запрос без ожидания ответа - закомичено
                            //System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                            //if (Encoding.GetEncoding(1251).GetString(ARMbyteArr) != "<Request method='get_static'/>") 
                            //{
                            //    _udpServer.Send(controllerCommand, controllerCommand.Length, _epContr31);    // запрос от драйвера к контроллеру
                            //}
                            #endregion

                            if (Encoding.GetEncoding(1251).GetString(ARMbyteArr) == "<Request method='get_static'/>")
                            {
                                try
                                {
                                    #region Если ресурсы _udpCts, _udpServer  закрыты - возобновляю 
                                    if (_udpCts is null)
                                    {
                                        _udpCts = new CancellationTokenSource();
                                    }

                                    if (_udpServer is null)
                                    {
                                        _udpServer = new UdpClient(Int32.Parse(_d["vesy31port"]));
                                    }
                                    #endregion

                                    while (!_udpCts.Token.IsCancellationRequested)
                                    {
                                        // Use Task.Run to allow for cancellation checks
                                        var receiveTask = _udpServer.ReceiveAsync();

                                        // Wait for data or handle timeout
                                        if (await Task.WhenAny(receiveTask, Task.Delay(5000, _udpCts.Token)) == receiveTask)
                                        {
                                            // Data received
                                            var result = receiveTask.Result;
                                            if ((result.Buffer != null) && (result.Buffer.Length == 93))
                                            {
                                                UdpDecoder rawResultOfValue = new UdpDecoder(result, _d["determiningTheEndian"]);
                                                ControllerMessage preparedMess = rawResultOfValue.GetPreparedMessage();

                                                if (preparedMess.wasError == false)
                                                {
                                                    byte[] xmlByteValues = XMLFormatter.getStatic(preparedMess.setOfValues);        // подгатавливаю XML документ
                                                    ARMsocket.Send(xmlByteValues, xmlByteValues.Length, SocketFlags.None);          // Отправляем в АРМ весов XML в виде byte[].
                                                    transferOccured = true;
                                                    _udpCts.Cancel();                                                           // после однократной операции - останавливаю передачу
                                                    _udpServer.Close();
                                                }
                                                else
                                                {
                                                    Exception exInner = new Exception("Ошибка формата документа");                  // Согласно спецификации - ловить таймаут
                                                    byte[] errorByteArr = XMLFormatter.GetError(exInner, 1);                        // Отформатировал ошибку в XML формат. 
                                                    ARMsocket.Send(errorByteArr, errorByteArr.Length, SocketFlags.None);            // Отправляем в АРМ весов XML в виде byte[].
                                                    transferOccured = true;
                                                    _udpCts.Cancel();                                                           // после однократной операции - останавливаю передачу
                                                    _udpServer.Close();
                                                }
                                            }
                                        }
                                        else
                                        {
                                            _udpCts.Cancel();                                                           // после однократной операции - останавливаю передачу
                                            _udpServer.Close();
                                            logger.Info($"No data received within timeout period");
                                        }
                                    }
                                }
                                catch (OperationCanceledException ex)
                                {
                                    logger.Info($"UDP server has been stopped\n {ex.Message}");
                                }
                                catch (SocketException ex)
                                {
                                    logger.Info($"Socket error:\n {ex.Message}");
                                }
                                catch (Exception ex)
                                {
                                    logger.Info($"Unexpected error:\n {ex.Message}");
                                }
                                finally
                                {
                                    if (_udpCts != null) _udpCts.Cancel();
                                    if (_udpServer != null) _udpServer.Close();
                                    _udpCts = null;
                                    _udpServer = null;

                                    logger.Info($"UDP server resources have been released \n");
                                }
                            }
                        }
                        catch (SocketException ex)
                        {
                            if (ex.SocketErrorCode == SocketError.TimedOut)
                            {
                                Exception exInner = new Exception("Прибор не ответил на запрос");           // Согласно спецификации - ловить таймаут
                                byte[] errorByteArr = XMLFormatter.GetError(exInner, 1);                    // Отформатировал ошибку в XML формат. 
                                ARMsocket.Send(errorByteArr, errorByteArr.Length, SocketFlags.None);        // Отправляем в АРМ весов XML в виде byte[].
                            }
                            logger.Error(ex);
                        }
                    }
                    else                                                                        // формирование для клиента сообщения об ошибке в его запросе.
                    {
                        Exception ex = new Exception("Нет ответа от устройства");               //
                        byte[] errorByteArr = XMLFormatter.GetError(ex, 3);                     // Отформатировал ошибку в XML формат. 
                        ARMsocket.Send(errorByteArr, errorByteArr.Length, SocketFlags.None);    // Отправляем в АРМ весов XML в виде byte[].
                        logger.Error(Encoding.GetEncoding(1251).GetString(errorByteArr));       // 
                    }
                }

                if (ARMsocket.Poll(3000, SelectMode.SelectRead) & ARMsocket.Available == 0)
                {
                    logger.Info("Lost connection to weighter ARM " + ARMsocket.RemoteEndPoint.ToString());
                    _keepOpen = false;
                }

                if (!transferOccured)
                    Thread.Sleep(1);
            }

            logger.Info("Stopping UDP server...");
            if (_udpServer != null) _udpServer.Close();
            if (_udpCts != null) _udpCts.Cancel();

            logger.Info("Connect to weighter ARM is closed " + ARMsocket.RemoteEndPoint.ToString()); // отключение от ARM весов
            ARMsocket.Close();
            _isfree = true;
        }

        private int LimitTo(int i, int limit) => i > limit ? limit : i;

        private byte[] DecodeClientRequestToControllerCommand(byte[] cliBuffer, int dataLength)
        {
            string controllerCommand = string.Empty;
            byte[] clientComandArr = new byte[dataLength];                     // установил размерность массива для команды клиента
            Buffer.BlockCopy(cliBuffer, 0, clientComandArr, 0, dataLength);    // копирую данные в промежуточный массив 

            switch (Encoding.GetEncoding(1251).GetString(clientComandArr))
            {
                case "<Request method='set_mode' parameter='Static'/>":     // 1)
                    controllerCommand = null;
                    break;

                case "<Request method='checksum'/>":                        // 2)
                    controllerCommand = null;
                    break;

                case "<Request method='get_static'/>":                      // 3) получить вес, взвешивание в статике
                    controllerCommand = "getWeight";
                    break;

                case "<Request method='set_zero' parameter='0'/>":          // 4)
                    controllerCommand = null;
                    break;

                case "<Request method='restart_weight'/>":                  // 5)
                    controllerCommand = null;
                    break;

                default:                                                    // 6) Команда полученная от клиента - не распознана.
                    controllerCommand = null;
                    break;
            }

            if (!string.IsNullOrEmpty(controllerCommand) && controllerCommand.Length > 0)
            {
                return Encoding.GetEncoding(1251).GetBytes(controllerCommand);
            }
            else
            {
                return null;
            }
        }
    }
}
