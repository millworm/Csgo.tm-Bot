namespace MonoTM2.InputOutput
{
    public enum MessageType
    {
        /// <summary>
        /// Информационные сообщения
        /// </summary>
        Info = 0,

        /// <summary>
        /// Предметы, которые не успел купить по уведомлениям
        /// </summary>
        Socket,

        /// <summary>
        /// "Забрать вещи"
        /// </summary>
        GetWeapon,

        /// <summary>
        /// "Передать вещи"
        /// </summary>
        GiveWeapon,

        /// <summary>
        /// Сообщения о покупке предметов
        /// </summary>
        BuyWeapon,

        /// <summary>
        /// Сообщения о завершении работы таймера
        /// </summary>
        Timer,

        /// <summary>
        /// Сообщения об ошибках
        /// </summary>
        Error,

        /// <summary>
        /// Стандартные сообщения
        /// </summary>
        Default,

        /// <summary>
        /// Запись логов
        /// </summary>
        Logs,
    }
}
