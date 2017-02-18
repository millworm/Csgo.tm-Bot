using System;


namespace MonoTM2
{
    class Program
    {
        static void Main()
        {
            CBot bot = new CBot();
            //загрузка всех настроек
            bot.Loader();
            //стартуем
            bot.Start();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Запуск выполнен");
            Console.ResetColor();
            string Mess = "";
            while (Mess != "q")
            {
                Mess = Console.ReadLine();
                try
                {
                    switch (Mess.ToLower())
                    {
						//показывать какие предметы были выставлены из списка покупок
                        case "w":
                            if (bot.exhibitMessage)
                                bot.exhibitMessage = false;
                            else
                                bot.exhibitMessage = true;
                            Console.WriteLine(bot.exhibitMessage.ToString());
                            break;
                        case "p":
                        case "profit":
                            Console.WriteLine(bot.GetProfit());
                            break;
						//подтвердить трейды
                        //case "trade":
                        //case "t":
                        //    if (bot.GetConfirmTradesValue())
                        //    {
                        //        bot.ConfirmTrade();
                        //    }
                        //    else
                        //    {
                        //        bot.TurnOnOrOffAutoConfirmTrades();
                        //    }
                        //    break;
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
                            foreach (var y in tempItems)
                                Console.WriteLine("[{3}] {0} {1} {2}", y.name, y.price, y.count, n++);
                            Console.ResetColor();
                            break;
                        
						//удалить предмет
                        case "delete":
                        case "d":
                            Console.WriteLine("Введите номер");
                            var N = Convert.ToInt16(Console.ReadLine());
                            Console.WriteLine("Удалить {0}  ?", bot.GetItem(N).name);
                            var A = Console.ReadLine().ToLower();
                            if (A == "y")
                                bot.RemoveItem(N);
                            Console.WriteLine("Удалено");                      
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
                    }
                }
                catch (Exception tty)
                {
                    Console.WriteLine(tty.Message);
                }
            }
            bot.Closing();
        }

    }
    
}
