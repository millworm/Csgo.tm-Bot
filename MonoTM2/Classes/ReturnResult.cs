namespace MonoTM2.Classes
{
    public class ReturnResult<T>
    {
        public bool success { get; set; }
        public string errorMessage { get; set; }
        public T dataResult { get; set; }
    }
}
