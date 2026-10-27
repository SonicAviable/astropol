namespace StellarisClone.Core
{
    public static class Loc
    {
        public static string T(string s) => s;
        public static string F(string s, params object[] a) => string.Format(s, a);
    }
}
