using System;


namespace MonoTM2
{
	static class Program
    {
        static void Main()
        {
            CBot bot = new CBot();
            //загрузка всех настроек
            bot.Loader();
            //стартуем
            bot.Start();

            Console.WriteLine("Запуск выполнен");
            string Mess = "";
            while (Mess != "q")
            {
                Mess = Console.ReadLine();
                try
                {
                    switch (Mess.ToLowerInvariant())
                    {
                        case "c":
                            Console.Clear();
                            break;
                        case "accept":
                            bot.AcceptMobileOrdersFunc();
                            break;
                        case "acceptmobile":
                            bot.AcceptMobileOrders();
                            break;
                        //привязка мобильного приложения 
                        case "mobile":
                            bot.CreateAuth();
                            break;
                        case "guard":
                            bot.GenerateGuardCode();
                            break;
                        //показывать какие предметы были выставлены из списка покупок
                        case "p":
                        case "profit":
                            Console.WriteLine(bot.GetProfit());
                            break;
                        //подтвердить трейды
                        case "trade":
                        case "t":
                            if (bot.GetConfirmTradesValue())
                            {
                                bot.ConfirmTrade();
                                Console.WriteLine("Выключить прием?");
                                var a = Console.ReadLine();

                                if (a.ToLowerInvariant() == "y")
                                {
                                    bot.TurnOnOrOffAutoConfirmTrades();

                                    if (bot.GetConfirmTradesValue())
                                    {
                                        Console.WriteLine("Отключено");
                                    }
                                }
                            }
                            else
                            {
                                bot.TurnOnOrOffAutoConfirmTrades();
                            }
                            break;
                        //добавить предмет
                        case "add":
                        case "a":
                            Console.WriteLine("Введите ссылку");
                            var link = Console.ReadLine();
                            bot.AddItem(link);
                            break;
                        //обноление
                        case "update":
                            //обноление цен
                            bot.UpdatePrice();
                            //проверка быстрых покупок
                            bot.CheckQuickOrders();
                            //обновление ордеров и оповщений
                            bot.UpdateOrdersAndNotifications();

                            break;
                        //вывести прибыль с предмета
                        case "gprice":
                            Console.WriteLine(bot.GetPrice());
                            break;
                        case "sprice":
                            Console.WriteLine("Введите прибыль");
                            bot.SetPrice(Console.ReadLine());
                            Console.WriteLine("Новая прибыль " + bot.GetPrice().ToString());

                            break;
                        //вывести список предметов	
                        case "list":
                            Console.ForegroundColor = ConsoleColor.Magenta;
                            int n = 0;
                            var tempItems = bot.GetListItems();
                            var profitList = bot.GetPrice();

                            Console.WriteLine($" №    {string.Format("{0,-45}", "Название")}\tЦена\tПрибыль\tТип");
                            foreach (var y in tempItems)
                            {
                                var nameList = string.Format("{0,-45}", y.name);
                                string typeList = Convert.ToString(y.priceCheck).Substring(0, 5);
                                if (y.profit != 0)
                                {
                                    Console.WriteLine("[{3}]  {0}\t{1}\t{2}\t{4}", nameList, y.price, y.profit, n++.ToString("00"), typeList);
                                }
                                else
                                {
                                    Console.WriteLine("[{3}]  {0}\t{1}\t{2}\t{4}", nameList, y.price, profitList, n++.ToString("00"), typeList);
                                }

                            }
                            Console.ResetColor();
                            break;

                        //удалить предмет
                        case "delete":
                        case "d":
                            Console.WriteLine("Введите номер");
                            var N = Convert.ToInt16(Console.ReadLine());
                            Console.WriteLine("Удалить {0}  ?", bot.GetItem(N).name);
                            var A = Console.ReadLine().ToLowerInvariant();
                            if (A == "y")
                            {
                                bot.RemoveItem(N);
                                Console.WriteLine("Удалено");
                            }

                            break;
                        case "stime":
                            Console.WriteLine("Введите время");
                            bot.SetUpdateTimer(Console.ReadLine());
                            Console.WriteLine("Новое время " + bot.GetUpdateTimer().ToString());
                            break;
                        case "gtime":
                            Console.WriteLine("Время " + bot.GetUpdateTimer().ToString());
                            break;
                        case "status":
                            Console.WriteLine(bot.Status());
                            break;
                        case "sprofit":
                            Console.WriteLine("Введите номер вещи и прибыль через пробел (пример: 2 20).\nДля использования общей прибыли выставить 0");
                            var line = Console.ReadLine();
                            var data = line.Split();
                            if (data.Length != 2)
                            {
                                Console.WriteLine("Неверный формат");
                            }
                            else
                            {
                                var id = Convert.ToInt32(data[0]);
                                var profit = Convert.ToInt32(data[1]);

                                if (id >= bot.GetListItems().Count || id < 0)
                                {
                                    Console.WriteLine("Номер за пределами списка вещей");
                                    break;
                                }
                                if (profit < 0)
                                {
                                    Console.WriteLine("Прибыль ниже нуля");
                                    break;
                                }

                                var item = bot.GetItem(id);
                                Console.WriteLine($"Выставить прибыль для {item.name} прибыль в {profit} копеек?");

                                var spC = Console.ReadLine().ToLowerInvariant();
                                if (spC == "y")
                                {
                                    if (bot.SetProfit(id, profit))
                                        Console.WriteLine("Выставлено");
                                    else
                                        Console.WriteLine("Ошибка");
                                }
                            }
                            break;
                        case "scheck":
                            Console.WriteLine("Сменить способ проверки цен для предмета.\nВведите номер предмета и тип проверки (0 - обычная, 1 - уведомления)\nПример: 1 1");
                            line = Console.ReadLine();
                            data = line.Split();
                            if (data.Length != 2)
                            {
                                Console.WriteLine("Неверный формат");
                                break;
                            }

                            var _id = Convert.ToInt32(data[0]);
                            var type = Convert.ToInt32(data[1]);
                            if (_id >= bot.GetListItems().Count || _id < 0)
                            {
                                Console.WriteLine("Номер за пределами списка вещей");
                                break;
                            }
                            if (type < 0 || type > 1)
                            {
                                Console.WriteLine("Неизвестный тип");
                                break;
                            }

                            bot.SetPriceCheck(_id, type);
                            break;
                        case "gcheck":
                            Console.WriteLine("Введите номер предмета");
                            var gcheckId = Convert.ToInt32(Console.ReadLine());
                            if (gcheckId >= bot.GetListItems().Count || gcheckId < 0)
                            {
                                Console.WriteLine("Номер за пределами списка вещей");
                            }
                            else
                            {
                                var typeCheck = bot.GetPriceCheck(gcheckId);
                                switch (typeCheck)
                                {
                                    case PriceCheck.Notification:
                                        Console.WriteLine("Уведомления");
                                        break;
                                    case PriceCheck.Price:
                                        Console.WriteLine("Обычный");
                                        break;
                                }
                            }
                            break;

                        default:
                            Console.WriteLine("Команда неизвестна");
                            break;
                    }
                }
                catch (Exception tty)
                {
                    Console.WriteLine(tty.Message);
                }
            }
        }

    }

}
