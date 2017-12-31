using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading;
using SteamToolkit.Trading;
using SteamToolkit.Web;
using SteamAuth;

namespace MonoTM2
{
	class TradeWorker
    {
        IAsyncResult IIncomingTradeResult, IOutgoingTradeResult;
        delegate void TradeDelegate();

        TradeDelegate IncomingTradeDelegate, OutgoingTradeDelegate;

        long _timeLastLogin;
        Account _account;
        readonly Web Web = new Web(new SteamWebRequestHandler());
        Config _config;
        EconServiceHandler offerHandler;
        MarketHandler marketHandler;

        SteamGuardAccount _mobileAccount;

        functions client;
        bool accountFileExist = false;

        public TradeWorker()
        {
            IncomingTradeDelegate = new TradeDelegate(IncomingTrade);
            OutgoingTradeDelegate = new TradeDelegate(OutgoingTrade);

            client = new functions();

            if (File.Exists("account.maFile"))
            {
                accountFileExist = true;

                //Загружаем данные от мобильного аккаунта
                _mobileAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText("account.maFile"));
            }

            if (File.Exists("config.json"))
            {
                _config = Config.Reload();
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
                    if (IIncomingTradeResult == null || IIncomingTradeResult.IsCompleted == true)
                    {
                        IIncomingTradeResult = IncomingTradeDelegate.BeginInvoke(null, null);
                        IncomingTradeDelegate.EndInvoke(IIncomingTradeResult);
                    }
                    break;
                case TypeTrade.OUT:
                    if (IOutgoingTradeResult == null || IOutgoingTradeResult.IsCompleted == true)
                    {
                        IOutgoingTradeResult = OutgoingTradeDelegate.BeginInvoke(null, null);
                        OutgoingTradeDelegate.EndInvoke(IOutgoingTradeResult);
                    }
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
                var tradeList = client.MarketTrades(_config.key);

                if (tradeList?.success == true && tradeList.trades.Count != 0)
                {
                    foreach (var trade in tradeList.trades)
                    {
                        if (trade.dir == "out")
                        {
                            if (AcceptTrade(Convert.ToUInt32(trade.trade_id), Convert.ToUInt32(trade.bot_id)))
                            {
                                Console.WriteLine("Подтвержден");
                                client.UpdateInvent(_config.key);
                            }
                            //TODO дописать проверку на наличие обменов
                        }
                    }
                }
                else
                {
                    //Запрашиваем бота
                    Console.WriteLine("Запрашиваем бота");
                    var bot_id = client.GetBotID(_config.key);

                    if (bot_id != null && bot_id != "0")
                    {
                        //Запрашиваем данные оффера
                        Console.WriteLine("Запрашиваем оффер");
                        var offer = client.GetOffer(bot_id, _config.key);

                        if (offer?.success == true)
                        {
                            if (AcceptTrade(Convert.ToUInt32(offer.trade), Convert.ToUInt32(bot_id)))
                            {
                                Console.WriteLine("Подтвердили");
                                client.UpdateInvent(_config.key);
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
            try
            {
                if (accountFileExist)
                {
                    //проверяем наличие входящих трейдов от маркета
                    var tradeList = client.MarketTrades(_config.key);

                    if (tradeList?.success == true && tradeList.trades.Count != 0)
                    {
                        foreach (var trade in tradeList.trades)
                        {
                            if (trade.dir == "in")
                            {
                                var res = AcceptTrade(Convert.ToUInt32(trade.trade_id), Convert.ToUInt32(trade.bot_id));
                                Thread.Sleep(2000);

                                AcceptConfirmations();
                            }
                        }
                    }
                    else
                    {
                        //Получаем оффер
                        Console.WriteLine("Получаем оффер");
                        var offer = client.GetOffer("1", _config.key, "in");

                        if (offer?.success == true)
                        {
                            //Подтверждаем его в стиме
                            Console.WriteLine("Подтверждаем");

                            var res = AcceptTrade(Convert.ToUInt32(offer.trade), Convert.ToUInt32(offer.botid));
                            Thread.Sleep(2000);

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
                if (answer?.TradeId != null)
                {
                    return true;
                }
                return false;
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
                _account = Web.RetryDoLogin(TimeSpan.FromSeconds(5), 10, _config.SteamLogin, _config.SteamPassword, _config.SteamMachineAuth);

                if (!string.IsNullOrEmpty(_account.SteamMachineAuth))
                {
                    _config.SteamMachineAuth = _account.SteamMachineAuth;
                    Config.Save(_config);
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
        void AcceptConfirmations(string nick)
        {
            try
            {
                _mobileAccount.Session.SteamLogin = _account.FindCookieByName("steamlogin").Value;
                _mobileAccount.Session.SteamLoginSecure = _account.FindCookieByName("steamloginsecure").Value;

                _mobileAccount.AcceptConfirmation(new Confirmation() { });
                foreach (Confirmation confirmation in _mobileAccount.FetchConfirmations())
                {
                    //Подтверждение трейдов не с маркета

                   // if (confirmation.Description.ToLower().Contains(nick.ToLower()))
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

        //Подтверждение трейдов не с маркета
        void AcceptConfirmations()
        {
            try
            {
                _mobileAccount.Session.SteamLogin = _account.FindCookieByName("steamlogin").Value;
                _mobileAccount.Session.SteamLoginSecure = _account.FindCookieByName("steamloginsecure").Value;

                _mobileAccount.AcceptConfirmation(new Confirmation() { });
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

