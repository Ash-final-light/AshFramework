namespace AshFramework.UI.UIModule.Examples.StackDemo
{
    public sealed class StackWindowStatistics
    {
        public int Creates { get; private set; }
        public int Releases { get; private set; }
        public int Binds { get; private set; }

        public void Created()
        {
            Creates++;
        }

        public void Released()
        {
            Releases++;
        }

        public void Bound()
        {
            Binds++;
        }
    }
}
