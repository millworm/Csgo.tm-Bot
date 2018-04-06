using Newtonsoft.Json;
using SteamAuth;
using SteamToolkit.Trading;
using SteamToolkit.Web;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MonoTM2.Classes;
using MonoTM2.InputOutput;
using Newtonsoft.Json.Linq;

namespace MonoTM2
{
    class TradeWorker
    {
        IAsyncResult IIncomingTradeResult, IOutgoingTradeResult;

        private static Semaphore _incomingTradeCounter, _outgoingTradeCounter;

        public event EventHandler OnOutgoingTrade, OnIncomingTrade;

        private static Dictionary<uint, TypeTrade> _offers;
        private delegate void TradeDelegate(string host);

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
            _offers = new Dictionary<uint, TypeTrade>();

            _incomingTradeCounter = new Semaphore(3, 4);
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
                if (!string.IsNullOrEmpty(_config.SteamSettings.Login) && !string.IsNullOrEmpty(_config.SteamSettings.Password))
                   new Task(Auth).Start();
            }

        }

        /// <summary>
        /// Подтвердить трейд
        /// </summary>
        /// <param name="type">IN - передача предмета который был продан; OUT - вывод предмета, который был куплен.</param>
        public void AcceptTrade(TypeTrade type, string host)
        {
            switch (type)
            {
                case TypeTrade.IN:
                    _incomingTradeDelegate.BeginInvoke(host,res => OnIncomingTrade?.Invoke(host, null), null);
                    /*if (IIncomingTradeResult == null || IIncomingTradeResult.IsCompleted)
                    {
                        IIncomingTradeResult = _incomingTradeDelegate.BeginInvoke(null, null);
                        _incomingTradeDelegate.EndInvoke(IIncomingTradeResult);
                    }*/
                    break;
                case TypeTrade.OUT:
                    _outgoingTradeDelegate.BeginInvoke(host,res => OnOutgoingTrade?.Invoke(host, null), null);
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
        void OutgoingTrade(string host)
        {
           try
            {
                //проверяем наличие входящих трейдов от маркета
               /* var tradeList = Functions.MarketTrades(host, _config.key);

                if (tradeList?.success == true && tradeList.trades.Count != 0)
                {
                    var unCheckedTrades = tradeList.trades.FindAll(itm =>
                        !_offers.ContainsKey(Convert.ToUInt32(itm.trade_id)) && itm.dir == "out");
                    foreach (var trade in unCheckedTrades)
                    {
                        AcceptTrade(Convert.ToUInt32(trade.trade_id), Convert.ToUInt32(trade.bot_id), TypeTrade.OUT);
                        //TODO дописать проверку на наличие обменов
                    }
                }*/

                //Запрашиваем бота
                ConsoleInputOutput.OutputMessage("Запрашиваем бота");
                var bot_id = Functions.GetBotId(host, _config.key);

                if (bot_id != null && bot_id != "0")
                {
                    //Запрашиваем данные оффера
                    ConsoleInputOutput.OutputMessage("Запрашиваем оффер");
                    var offer = Functions.GetOffer(bot_id, host, _config.key);

                    if (offer?.success == true)
                    {
                        AcceptTrade(Convert.ToUInt32(offer.trade), Convert.ToUInt32(bot_id), TypeTrade.OUT);
                        Functions.UpdateInvent(host, _config.key);
                    }
                }

            }
            catch (Exception ex)
            {
                ConsoleInputOutput.OutputMessage($"{new string('-', 50)}\nMessage:{ex.Message}\nTarget:{ex.StackTrace}\n{new string('-', 50)}");
            }
        }

        /// <summary>
        /// Трейды на передачу проданного
        /// </summary>
        void IncomingTrade(string host)
        {
            _incomingTradeCounter.WaitOne();
            try
            {
                if (accountFileExist)
                {
                    //проверяем наличие входящих трейдов от маркета
                 /*   var tradeList = Functions.MarketTrades(host, _config.key);

                    if (tradeList?.success == true && tradeList.trades.Count != 0)
                    {
                        var unCheckedTrades = tradeList.trades.FindAll(itm =>
                            !_offers.ContainsKey(Convert.ToUInt32(itm.trade_id)) && itm.dir == "out");
                        foreach (var trade in unCheckedTrades)
                        {
                            AcceptTrade(Convert.ToUInt32(trade.trade_id), Convert.ToUInt32(trade.bot_id), TypeTrade.IN);
                            AcceptConfirmations();
                        }
                    }*/
                    //Получаем оффер
                    ConsoleInputOutput.OutputMessage("Получаем оффер");
                    var offer = Functions.GetOffer("1", host, _config.key, "in");

                    if (offer?.success == true)
                    {
                        var tradeId = Convert.ToUInt32(offer.trade);
                        var botId = Convert.ToUInt32(offer.botid);

                        //Подтверждаем его в стиме
                        ConsoleInputOutput.OutputMessage("Подтверждаем");

                        var res = AcceptTrade(tradeId, botId, TypeTrade.IN);

                        //Подтверждаем в мобильной версии
                        AcceptConfirmations();
                        Functions.UpdateInvent(host, _config.key);

                    }
                }
            }
            catch (Exception ex)
            {
                ConsoleInputOutput.OutputMessage($"{new string('-', 50)}\nMessage:{ex.Message}\nTarget:{ex.StackTrace}\n{new string('-', 50)}");
            }
            finally
            {
               
                _incomingTradeCounter.Release();
            }
        }

        /// <summary>
        /// Подтверждаем трейд
        /// </summary>
        /// <param name="tradeId">id трейда</param>
        /// <param name="botId">id бота</param>
        /// <returns>True - если обмен принят, иначе False</returns>
        bool AcceptTrade(uint tradeId, uint botId, TypeTrade type)
        {
            if (_offers.ContainsKey(tradeId)) return false;
            _offers.Add(tradeId, type);
            try
            {
                marketHandler.EligibilityCheck(_account.SteamId, _account.AuthContainer);

                var answer = offerHandler.AcceptTradeOffer(tradeId, botId, _account.AuthContainer, "1");
                return answer?.TradeId != null || answer.MobileConfirmation || answer.EmailConfirmation;
            }

            catch (NullReferenceException)
            {
                var nowTime = DateTimeOffset.Now.ToUnixTimeSeconds();
                if (nowTime - _timeLastLogin > _config.SteamSettings.TimeoutRelogin)
                {
                    ConsoleInputOutput.OutputMessage("Переавторизовываемся");
                    Auth();
                }
                return false;

            }
            catch (Exception ex)
            {
                ConsoleInputOutput.OutputMessage($"{new string('-', 50)}\nMessage:{ex.Message}\nTarget:{ex.StackTrace}\n{new string('-', 50)}");
                return false;
            }

            finally
            {
                _offers.Remove(tradeId);
            }
        }

        /// <summary>
        /// Авторизация в стиме
        /// </summary>
        public async void Auth()
        {
            try
            {
                _account = Web.RetryDoLogin(TimeSpan.FromSeconds(5), 1, _config.SteamSettings.Login, _config.SteamSettings.Password, _config.SteamSettings.MachineAuth);

                if (!string.IsNullOrEmpty(_account.SteamMachineAuth))
                {
                    _config.SteamSettings.MachineAuth = _account.SteamMachineAuth;
                    Config.Save();
                }

                ConsoleInputOutput.OutputMessage("Авторизован!");

                _timeLastLogin = DateTimeOffset.Now.ToUnixTimeSeconds();

                offerHandler = new EconServiceHandler("");
                marketHandler = new MarketHandler();
                marketHandler.EligibilityCheck(_account.SteamId, _account.AuthContainer);
            }

            catch (Exception ex)
            {
                ConsoleInputOutput.OutputMessage(ex.Message);
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
        //                    ConsoleInputOutput.OutputMessage("Подтвержден в приложении");
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
                        ConsoleInputOutput.OutputMessage("Подтвержден в приложении");
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

