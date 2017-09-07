using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
namespace MonoTM2
{

    class functions
    {
        readonly string host = "https://market.csgo.com";
        //поиск вещи
        public bool Buy(Itm item, string key, int timeout = 200)
        {
            int i = 0;
            try
            {
                a:
                //string resp = Web(host + "/api/Buy/" + item.id + "/" + Convert.ToInt32(item.price * 100) + "/" + item.hash + "/" + "?key=" + key, timeout);
                string resp = Web(host + "/api/Buy/" + item.id + "/" + Convert.ToInt32(item.price * 100) + "/" + "?key=" + key, timeout);
                aBuy it = JsonConvert.DeserializeObject<aBuy>(resp);
                if (it.id != null && it.id.ToLower() != "false")
                {
                    if (it.result == "ok")
                    {
                        return true;
                    }
                }
                else
                {
                    if (it.error != null && it.error.ToLower() == "bad key")
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("неверный ключ");
                        Console.ResetColor();
                        Console.ReadKey();
                        Environment.Exit(-1);
                    }
                    if (it.result.ToLower().IndexOf("изменилось") != -1 || it.result.ToLower().IndexOf("вывод") != -1 || it.result.ToLower().IndexOf("средств") != -1 || it.result.ToLower().IndexOf("инвентарь") != -1 || it.result.ToLower().IndexOf("боты") != -1)
                    {
                        return false;
                    }
                    if (it.result.IndexOf("устарело") == -1 && i++ < 5)
                    {
                        // Console.WriteLine(string.Format("[{0}] {1}", item.name, it.result));
                        goto a;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }

        }
        //
        //получить список выщей на продаже
        public Trades GetTrades(string key)
        {

            try
            {
                string resp = Web(host + "/api/Trades/?key=" + key);
                resp = resp.Insert(0, "{\"items\":");
                resp += "}";
                return JsonConvert.DeserializeObject<Trades>(resp);
            }

            catch
            {
                return new Trades();
            }
        }
        //Получить hash предмета
        /// <summary>
        /// Получает хеш предмета и заполняет название
        /// </summary>
        /// <param name="item">предмет для которого нужно получить хеш и заполнить название</param>
        /// <param name="key">Api key</param>
        /// <param name="lang">en,ru</param>
        /// <returns></returns>
        public string GetHash(Itm item, string key, string lang = "en")
        {
            string pattern = "[0-9]{1,15}[-]{1}[0-9]{1,15}";
            var match = System.Text.RegularExpressions.Regex.Match(item.link, pattern);
            var code = match.Value.Replace("-", "_");

            string resp = Web(host + "/api/ItemInfo/" + code + "/" + lang + "/?key=" + key, 5000);
            Info inf = JsonConvert.DeserializeObject<Info>(resp);

            item.name = inf.name + " (" + inf.quality + ")";
            item.id = code;
            item.hash = inf.hash;
            return inf.hash;
        }
        //
        //Запрос мин. цены
        public string GetMinPrice(Itm item, string key)
        {
            string resp = Web(host + "/api/BestSellOffer/" + item.id + "/?key=" + key);

            var BestSellOffer = new { success = false, best_offer = "", error = "" };
            var Message = JsonConvert.DeserializeAnonymousType(resp, BestSellOffer);
            if (Message != null && Message.error == null && Message.success)
            {
                return Message.best_offer;
            }
            else
            {
                return "-1";
            }
        }
        //
        //Запрос средней цены
        public string GetAverangePrice(Itm item, string key)
        {
            string resp = Web(host + "/api/ItemHistory/" + item.id + "/?key=" + key);
            var pr = new { success = "", average = "" };
            var inf = JsonConvert.DeserializeAnonymousType(resp, pr);
            if (inf.success == "True" || inf.success == "true")
            {
                return inf.average.ToString();
            }
            else
            {
                return "0";
            }
        }
        //
        //Запрос последней в истории цены
        public string GetLastPrice(Itm item, string key)
        {
            string resp = Web(host + "/api/ItemHistory/" + item.id + "/?key=" + key);
            var pr = new { success = "", average = "", history = new[] { new { l_price = "" } }, error = "" };
            var inf = JsonConvert.DeserializeAnonymousType(resp, pr);
            if (inf.success == "True" || inf.success == "true")
            {
                return (Convert.ToDouble(inf.history[0].l_price)).ToString();
            }
            else
            {
                return "0";
            }
        }
        //Запрос баланса
        public int GetMoney(string key)
        {
            try
            {
                string resp = Web(host + "/api/GetMoney/?key=" + key);
                Console.WriteLine("Баланс: {0}", resp);
                Money mon = JsonConvert.DeserializeObject<Money>(resp);
                if (mon.error == "Bad KEY")
                {
                    return -1;
                }
                else
                {
                    return mon.money;
                }
            }
            catch
            {
                return 0;
            }
        }

        ///Запрос оффера
        public Trade GetOffer(string bid, string key, string o = "out")
        {
            try
            {
                string resp = Web(host + "/api/ItemRequest/" + o + "/" + bid + "/?key=" + key);
                Trade tr = JsonConvert.DeserializeObject<Trade>(resp);
                if (tr.success)
                    return tr;
                else
                {
                    if (tr.error != null && tr.error.ToLower().IndexOf("загрузить") != -1)
                    {
                        UpdateInvent(key);
                        return null;
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            catch
            {
                return null;
            }
        }
        //
        public string GetBotID(string key)
        {
            try
            {
                string resp = Web(host + "/api/Trades/?key=" + key);
                var trades = JsonConvert.DeserializeObject<List<TradeOffer>>(resp);
                foreach (var t in trades)
                {
                    if (t.ui_bid != "0" && t.ui_status == "4")
                    {
                        return t.ui_bid;
                    }

                }
                return "0";
            }
            catch
            {
                return "0";
            }
        }
        //Пинг
        public void Ping(string key)
        {
            Web(host + "/api/PingPong/?key=" + key);
        }

        /// <summary>
        /// Получение ключа для сообщений сокетов
        /// </summary>
        /// <param name="key">api key</param>
        /// <returns>string socket key</returns>
        public string GetWS(string key)
        {
            return Web(host + "/api/GetWSAuth/?key=" + key);
        }
        //

        //Веб запрос
        public static string Web(string server, int timeout = 500)
        {
            try
            {
                HttpWebRequest myHttpWebRequest = (HttpWebRequest)HttpWebRequest.Create(server);
                HttpWebResponse myHttpWebResponse = (HttpWebResponse)myHttpWebRequest.GetResponse();
                myHttpWebRequest.Timeout = timeout;
                StreamReader myStreamReader = new StreamReader(myHttpWebResponse.GetResponseStream());
                return myStreamReader.ReadToEnd();
            }
            catch (WebException e)
            {
                switch (e.Status)
                {
                    case WebExceptionStatus.ProtocolError:
                    case WebExceptionStatus.Timeout:
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(e.Message);
                        Console.ResetColor();
                        break;
                }
                return "{\"id\":\"False\"}";
            }
        }


        //получения списка быстрых покупок
        public List<QItems> QList(string key)
        {
            try
            {
                string answer = Web(host + "/api/QuickItems/?key=" + key);
                var QData = JsonConvert.DeserializeObject<QData>(answer);
                if (QData != null && QData.success)
                {
                    return QData.items;
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        //быстрая покупка
        public bool QBuy(string key, string id)
        {
            try
            {
                string answer = Web(host + "/api/QuickBuy/" + id + "/?key=" + key);
                var b = JsonConvert.DeserializeObject<QAnswer>(answer);
                if (b.success)
                {
                    return true;
                }
                else
                {
                    return false;
                }

            }
            catch
            {
                return false;
            }
        }

        //получить список уже отправленных трейдов
        public MarketTrades MarketTrades(string key)
        {
            try
            {
                string answer = Web(host + "/api/MarketTrades/?key=" + key);
                var b = JsonConvert.DeserializeObject<MarketTrades>(answer);
                if (b.success)
                {
                    return b;
                }
                else
                {
                    return null;
                }

            }
            catch
            {
                return null;
            }
        }

        //обновить инвентарь
        public void UpdateInvent(string key)
        {
            string answer = Web(host + "/api/UpdateInventory/?key=" + key);

            var status = new { success = false };
            var inf = JsonConvert.DeserializeAnonymousType(answer, status);

            if (!inf.success)
            {
                UpdateInvent(key);
            }
        }

        /// <summary>
        /// Удалить все ордеры на быструю покупку
        /// </summary>
        /// <param name="key">api key</param>
        /// <returns></returns>
        public bool DeleteAllOrders(string key)
        {
            try
            {
                string answer = Web(host + "/api/DeleteOrders/?key=" + key);
#if DEBUG
                System.Diagnostics.Debug.WriteLine(answer);
#endif
                var pr = new { success = false, deleted_orders = 0, error = "" };
                var inf = JsonConvert.DeserializeAnonymousType(answer, pr);
                if (inf.success == true)
                {
                    return true;
                }
                else
                {
                    return false;
                }

            }
            catch
            {
                return false;
            }
        }

        public string ProcessOrder(Itm item, string key)
        {
            try
            {
                //https://market.csgo.com/api/ProcessOrder/[classid]/[instanceid]/[price]/?key=[your_secret_key]
                string answer = Web(host + string.Format("/api/ProcessOrder/{0}/{1}/?key={2}", item.id.Replace('_', '/'), item.price * 100, key));
                var pr = new { success = false, way = "", error = "" };
                var inf = JsonConvert.DeserializeAnonymousType(answer, pr);
                if (inf.success == true)
                {
                    return inf.way;
                }
                else
                {
                    return inf.error;
                }

            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
        /// <summary>
        /// Подписка на уведомления в сокетах на выбранные предметы
        /// </summary>
        /// <param name="item">Предмет</param>
        /// <param name="key">api ключ</param>
        /// <returns>возвращает строку true, если все прошло без ошибок, иначе текст ошибки</returns>
        public string Notification(Itm item, string key, int price = 0)
        {
            try
            {
                //https://market.csgo.com/api/UpdateNotification/[classid]/[instanceid]/[price]?key=[your_secret_key]
                string answer;
                if (price == 0)
                {
                    answer = Web(host + string.Format("/api/UpdateNotification/{0}/{1}/?key={2}", item.id.Replace('_', '/'), item.price * 100, key));
                }
                else
                {
                    answer = Web(host + string.Format("/api/UpdateNotification/{0}/{1}/?key={2}", item.id.Replace('_', '/'), 0, key));
                }

                var pr = new { success = false, error = "" };
                var inf = JsonConvert.DeserializeAnonymousType(answer, pr);
                if (inf.success == true)
                {
                    return inf.success.ToString();
                }
                else
                {
                    return inf.error;
                }

            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// Запросить скидку
        /// </summary>
        /// <param name="key"></param>
        /// <returns>Если не было ошибок - скидку; если были, то значения 10.01 - 10.03</returns>
        public double GetDiscounts(string key)
        {
            try
            {
                //https://market.csgo.com/api/GetDiscounts/?key=[your_secret_key]
                string answer = Web(host + "/api/GetDiscounts/?key=" + key);

                var pr = new { success = false, discounts = new { sell_fee = "" }, error = "" };
                var inf = JsonConvert.DeserializeAnonymousType(answer, pr);
                if (inf.success == true)
                {
                    return Convert.ToDouble(inf.discounts.sell_fee.Replace("%", ""), System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    if (inf.error.ToLower() == "bad key")
                    {
                        return 10.01;
                    }
                    else
                    {
                        return 10.03;
                    }
                }

            }
            catch
            {
                return 10.02;
            }
        }

        /// <summary>
        /// Получение дневной прибыли
        /// </summary>
        /// <param name="key"></param>
        /// <returns>Примерная дневная прибыль в копейках</returns>
        public int GetProfit(string key)
        {
            try
            {
                int spent = 0, earned = 0;
                DateTime startDay = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0, 0, 1);
                DateTime endDay = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 23, 59, 59);
                //https://market.csgo.com/api/OperationHistory/[start_time]/[end_time]/?key=[your_secret_key]
                string answer = Web(host + string.Format("/api/OperationHistory/{0}/{1}/?key={2}", (int)startDay.Subtract(new DateTime(1970, 1, 1)).TotalSeconds, (int)endDay.Subtract(new DateTime(1970, 1, 1)).TotalSeconds, key));
                var pr = new { success = false, history = new[] { new { h_event = "", recieved = "", stage = "" } } };
                var inf = JsonConvert.DeserializeAnonymousType(answer, pr);
                if (inf.success == true && inf.history != null)
                {
                    foreach (var i in inf.history)
                    {
                        if (i.h_event == "sell_go")
                        {
                            earned += Convert.ToInt32(i.recieved);
                        }
                        if (i.h_event == "buy_go" && i.stage != "5")
                        {
                            spent += Convert.ToInt32(i.recieved);
                        }
                    }
                    return earned - spent;
                }
                else
                {
                    return -1;
                }

            }
            catch
            {
                return -2;
            }
        }

        public void GoOffline(string key)
        {
            // https://market.csgo.com/api/GoOffline/?key=
            Web(host + "/api/GoOffline/?key=" + key);
        }

        /// <summary>
        /// Проверяет сколько предметов на передачу и прием висит в профиле
        /// </summary>
        /// <param name="key">Api ключ</param>
        /// <returns>Возвращает переменную с двумя значениями: getCount - сколько нужно принять; outCount - сколько нужно передать</returns>
        public InventOffersInfo CountItemsToTransfer(string key)
        {
            try
            {
                var returnVariable = new InventOffersInfo();

                string resp = Web(host + "/api/Trades/?key=" + key);
                resp = resp.Insert(0, "{\"items\":");
                resp += "}";
                var items = JsonConvert.DeserializeObject<Trades>(resp);
                foreach (var item in items.items)
                {
                    if (item.ui_status == "4")
                    {
                        returnVariable.getCount++;
                    }

                    if (item.ui_status == "2")
                    {
                        returnVariable.outCount++;
                    }
                }

                return returnVariable;
            }

            catch
            {
                return null;
            }
        }
        /// <summary>
        /// Получить список включенных уведомлений о изменении цены
        /// </summary>
        /// <param name="key">Api key</param>
        public CNotifications GetNotifications(string key)
        {
            //https://market.csgo.com/api/GetNotifications/?key=[your_secret_key]
            try
            {
                string answer = Web(host + "/api/GetNotifications/?key=" + key);
                //var pr = new { success = false, Notifications = new { i_classid = "", i_instanceid ="", n_val="" }, error = "" };
                var inf = JsonConvert.DeserializeObject<CNotifications>(answer);
                if (inf.success == true)
                {
                    return inf;
                }
                else
                {
                    return null;
                }

            }
            catch
            {
                return null;
            }
        }

    }
}
