using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;
using System.Net;
namespace MonoTM2
{

    class functions
    {
        string host = "https://market.csgo.com";
        //поиск вещи
        public bool Buy(Itm item, string key)
        {
            int i = 0;
            try
            {
            a:
                string resp = Web(host + "/api/Buy/" + item.id + "/" + Convert.ToInt32(item.price * 100) + "/" + item.hash + "/" + "?key=" + key);
                aBuy it = JsonConvert.DeserializeObject<aBuy>(resp);
                if (it.id != "False")
                {
                    if (it.result == "ok")
                    {
                        return true;
                    }
                }
                else
                {
                    if (it.result.IndexOf("изменилось") != -1 || it.result.IndexOf("вывод") != -1 || it.result.IndexOf("средств") != -1 || it.result.IndexOf("инвентарь") != -1 || it.result.IndexOf("Боты") != -1)
                    {
                        return false;
                    }
                    if (it.result.IndexOf("устарело") == -1 && i++ < 3)
                    {

                        Console.WriteLine(string.Format("[{0}] {1}", item.name, it.result));
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

            catch (Exception text)
            {
                var yy = text.Message;
                return new Trades();
            }
        }
        //Получить hash предмета
        public string GetHash(Itm item, string key)
        {
            int start = item.link.IndexOf("item/") + 5;
            int end = item.link.IndexOf("-", start);
            end = item.link.IndexOf("-", end + 1);
            if (end == -1) end = item.link.Length;
            string code = item.link.Substring(start, end - start);
            code = code.Replace("-", "_");

            string resp = Web(host + "/api/ItemInfo/" + code + "/en/?key=" + key);
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
                return Message.best_offer;
            else
                return "-1";
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
                return "0";
        }
        //
        //Запрос последней в истории цены
        public string GetLastPrice(Itm item, string key)
        {

            string resp = Web(host + "/api/ItemHistory/" + item.id + "/?key=" + key);
            var pr = new { success = "", average = "", history = new[] { new { l_price = "" } } };
            // var pr = new { success = "", average = "" };
            var inf = JsonConvert.DeserializeAnonymousType(resp, pr);
            if (inf.success == "True" || inf.success == "true")
            {
                return (Convert.ToDouble(inf.history[0].l_price)).ToString();
            }
            else
                return "0";
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
                    return -1;
                else
                    return mon.money;
            }
            catch
            {
                return 0;
            }
        }

        //Запрос оффера
        public Trade GetOffer(string bid, string key)
        {
            try
            {
                string resp = Web(host + "/api/ItemRequest/out/" + bid + "/?key=" + key);
                Trade tr = JsonConvert.DeserializeObject<Trade>(resp);
                if (tr.success)
                    return tr;
                else
                    return null;
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
        public string Web(string server)
        {

            try
            {
                HttpWebRequest myHttpWebRequest = (HttpWebRequest)HttpWebRequest.Create(server);
                HttpWebResponse myHttpWebResponse = (HttpWebResponse)myHttpWebRequest.GetResponse();
                myHttpWebRequest.Timeout = 300;
                StreamReader myStreamReader = new StreamReader(myHttpWebResponse.GetResponseStream());
                return myStreamReader.ReadToEnd();
                // Выполняем запрос по адресу и получаем ответ в виде строки
                /*  var webClient = new WebClient();
                  return webClient.DownloadString(server);*/
            }
            catch (WebException e)
            {
                switch (e.Status)
                {
                    case WebExceptionStatus.ProtocolError:
                        break;
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
            Web(host + "/api/UpdateInventory/?key=" + key);
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
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine(answer);
//#endif
                var pr = new { success = false, way = "", error = "" };
                var inf = JsonConvert.DeserializeAnonymousType(answer, pr);
                if (inf.success == true)
                {
                    return inf.way;
                }
                else
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("ProcessOrder " + inf.error);
#endif

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
                    answer = Web(host + string.Format("/api/UpdateNotification/{0}/{1}/?key={2}", item.id.Replace('_', '/'), item.price * 100, key));
                else
                    answer = Web(host + string.Format("/api/UpdateNotification/{0}/{1}/?key={2}", item.id.Replace('_', '/'), 0, key));

                var pr = new { success = false, error = "" };
                var inf = JsonConvert.DeserializeAnonymousType(answer, pr);
                if (inf.success == true)
                {
                    return inf.success.ToString();
                }
                else
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("Notification " + inf.error);
#endif
                    return inf.error;
                }

            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
