using Newtonsoft.Json;
using SteamAuth;
using SteamToolkit.Trading;
using SteamToolkit.Web;
using System;
using System.IO;
using System.Threading;

namespace MonoTM2
{
    class TradeWorker
    {
        IAsyncResult IIncomingTradeResult, IOutgoingTradeResult;

        private static Semaphore _incomingTradeCounter, _outgoingTradeCounter;

        public event EventHandler OnOutgoingTrade, OnIncomingTrade;

        private delegate void TradeDelegate();

        TradeDelegate _incomingTradeDelegate, _outgoingTradeDelegate;

        long _timeLastLogin;
        Account _account;
        readonly Web Web = new Web(new SteamWebRequestHandler());
        Config _config = Config.GetConfig();
        EconServiceHandler offerHandler;
        MarketHandler marketHandler;

        SteamGuardAccount _mobileAccount;

        //Functions client;
        bool accountFileExist;

        public TradeWorker()
        {
            _incomingTradeCounter = new Semaphore(3, 5);
            _incomingTradeDelegate = IncomingTrade;
            _outgoingTradeDelegate = OutgoingTrade;

            //client = new Functions();

            if (File.Exists("account.maFile"))
            {
                accountFileExist = true;

                //Загружаем данные от мобильного аккаунта
                _mobileAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText("account.maFile"));
            }

            if (File.Exists("config.json"))
            {
                if (!string.IsNullOrEmpty(_config.SteamLogin) && !string.IsNullOrEmpty(_config.SteamPassword))
                    Auth();
            }

        }

        /// <summary>
        /// Подтвердить трейд
        /// </summary>
        /// <param name="type">IN - передача предмета который был продан; OUT - вывод предмета, который был куплен.</param>
        public void AcceptTrade(TypeTrade type)
        {
            switch (type)
            {
                case TypeTrade.IN:
                    _incomingTradeDelegate.BeginInvoke(res => OnIncomingTrade?.Invoke(this, null), null);
                    /*if (IIncomingTradeResult == null || IIncomingTradeResult.IsCompleted)
                    {
                        IIncomingTradeResult = _incomingTradeDelegate.BeginInvoke(null, null);
                        _incomingTradeDelegate.EndInvoke(IIncomingTradeResult);
                    }*/
                    break;
                case TypeTrade.OUT:
                    _outgoingTradeDelegate.BeginInvoke(res => OnOutgoingTrade?.Invoke(this, null), null);
                    /*if (IOutgoingTradeResult == null || IOutgoingTradeResult.IsCompleted)
                    {
                        IOutgoingTradeResult = _outgoingTradeDelegate.BeginInvoke(EventStarter, null);
                        _outgoingTradeDelegate.EndInvoke(IOutgoingTradeResult);
                    }*/
                    break;
                case TypeTrade.MOBILE:
                    new System.Action(AcceptConfirmations).BeginInvoke(null, null);
                    break;
            }
        }

        /// <summary>
        /// Трейды на вывод
        /// </summary>
        void OutgoingTrade()
        {
            try
            {
                //проверяем наличие входящих трейдов от маркета
                var tradeList = Functions.MarketTrades(_config.key);

                if (tradeList?.success == true && tradeList.trades.Count != 0)
                {
                    foreach (var trade in tradeList.trades)
                    {
                        if (trade.dir == "out")
                        {
                            if (AcceptTrade(Convert.ToUInt32(trade.trade_id), Convert.ToUInt32(trade.bot_id)))
                            {
                                Console.WriteLine("Подтвержден");
                                Functions.UpdateInvent(_config.key);
                            }
                            //TODO дописать проверку на наличие обменов
                        }
                    }
                }
                else
                {
                    //Запрашиваем бота
                    Console.WriteLine("Запрашиваем бота");
                    var bot_id = Functions.GetBotId(_config.key);

                    if (bot_id != null && bot_id != "0")
                    {
                        //Запрашиваем данные оффера
                        Console.WriteLine("Запрашиваем оффер");
                        var offer = Functions.GetOffer(bot_id, _config.key);

                        if (offer?.success == true)
                        {
                            if (AcceptTrade(Convert.ToUInt32(offer.trade), Convert.ToUInt32(bot_id)))
                            {
                                Console.WriteLine("Подтвердили");
                                Functions.UpdateInvent(_config.key);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(new string('-', 50));
                Console.WriteLine($"Message:{ex.Message}\nTarget:{ex.StackTrace}");
                Console.WriteLine(new string('-', 50));
            }
        }

        /// <summary>
        /// Трейды на передачу проданного
        /// </summary>
        void IncomingTrade()
        {
            _incomingTradeCounter.WaitOne();
            try
            {
                if (accountFileExist)
                {
                    //проверяем наличие входящих трейдов от маркета
                    var tradeList = Functions.MarketTrades(_config.key);

                    if (tradeList?.success == true && tradeList.trades.Count != 0)
                    {
                        foreach (var trade in tradeList.trades)
                        {
                            if (trade.dir == "in")
                            {
                                var res = AcceptTrade(Convert.ToUInt32(trade.trade_id), Convert.ToUInt32(trade.bot_id));

                                AcceptConfirmations();
                            }
                        }
                    }
                    else
                    {
                        //Получаем оффер
                        Console.WriteLine("Получаем оффер");
                        var offer = Functions.GetOffer("1", _config.key, "in");

                        if (offer?.success == true)
                        {
                            //Подтверждаем его в стиме
                            Console.WriteLine("Подтверждаем");

                            var res = AcceptTrade(Convert.ToUInt32(offer.trade), Convert.ToUInt32(offer.botid));

                            //Подтверждаем в мобильной версии
                            AcceptConfirmations();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(new string('-', 50));
                Console.WriteLine($"Message:{ex.Message}\nTarget:{ex.StackTrace}");
                Console.WriteLine(new string('-', 50));
            }
            finally
            {
                _incomingTradeCounter.Release();
            }
        }

        /// <summary>
        /// Подтверждаем трейд
        /// </summary>
        /// <param name="trade_id">id трейда</param>
        /// <param name="bot_id">id бота</param>
        /// <param name="account">авторизованный аккаунт</param>
        /// <returns>True - если обмен принят, иначе False</returns>
        bool AcceptTrade(uint trade_id, uint bot_id)
        {
            try
            {
                marketHandler.EligibilityCheck(_account.SteamId, _account.AuthContainer);

                var answer = offerHandler.AcceptTradeOffer(trade_id, bot_id, _account.AuthContainer, "1");
                return answer?.TradeId != null || answer.MobileConfirmation || answer.EmailConfirmation;
            }

            catch (NullReferenceException)
            {
                var nowTime = DateTimeOffset.Now.ToUnixTimeSeconds();
                if (nowTime - _timeLastLogin > _config.SteamTimeOutRelogin)
                {
                    Console.WriteLine("Переавторизовываемся");
                    Auth();
                }
                return false;

            }
            catch (Exception ex)
            {
                Console.WriteLine(new string('-', 50));
                Console.WriteLine($"Message:{ex.Message}\nTarget:{ex.StackTrace}");
                Console.WriteLine(new string('-', 50));
                return false;
            }
        }

        /// <summary>
        /// Авторизация в стиме
        /// </summary>
        public void Auth()
        {
            try
            {
                _account = Web.RetryDoLogin(TimeSpan.FromSeconds(5), 1, _config.SteamLogin, _config.SteamPassword, _config.SteamMachineAuth);

                if (!string.IsNullOrEmpty(_account.SteamMachineAuth))
                {
                    _config.SteamMachineAuth = _account.SteamMachineAuth;
                    Config.Save();
                }

                Console.WriteLine("Авторизован!");
                
                _timeLastLogin = DateTimeOffset.Now.ToUnixTimeSeconds();

                offerHandler = new EconServiceHandler("");
                marketHandler = new MarketHandler();
                marketHandler.EligibilityCheck(_account.SteamId, _account.AuthContainer);
            }

            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }


        /// <summary>
        /// Подтверждение в мобильном приложении
        /// </summary>
        /// <param name="sgAccount"></param>
        //void AcceptConfirmations(string nick)
        //{
        //    try
        //    {
        //        _mobileAccount.Session.SteamLogin = _account.FindCookieByName("steamlogin").Value;
        //        _mobileAccount.Session.SteamLoginSecure = _account.FindCookieByName("steamloginsecure").Value;

        //        _mobileAccount.AcceptConfirmation(new Confirmation() { });
        //        foreach (Confirmation confirmation in _mobileAccount.FetchConfirmations())
        //        {
        //            //Подтверждение трейдов не с маркета

        //           // if (confirmation.Description.ToLower().Contains(nick.ToLower()))
        //                if (_mobileAccount.AcceptConfirmation(confirmation))
        //                {
        //                    Console.WriteLine("Подтвержден в приложении");
        //                }
        //        }
        //    }
        //    catch
        //    {

        //    }
        //}

        //Подтверждение трейдов не с маркета
        void AcceptConfirmations()
        {
            try
            {
                _mobileAccount.Session.SteamLogin = _account.FindCookieByName("steamlogin").Value;
                _mobileAccount.Session.SteamLoginSecure = _account.FindCookieByName("steamloginsecure").Value;

                //_mobileAccount.AcceptConfirmation(new Confirmation(0, 0, 1, 0));
                foreach (Confirmation confirmation in _mobileAccount.FetchConfirmations())
                {
                    if (_mobileAccount.AcceptConfirmation(confirmation))
                    {
                        Console.WriteLine("Подтвержден в приложении");
                    }
                }
            }
            catch
            {

            }
        }
    }

    /// <summary>
    /// IN - передача предмета который был продан; OUT - вывод предмета, который был куплен.
    /// </summary>
    enum TypeTrade
    {
        IN, OUT, MOBILE
    }
}

