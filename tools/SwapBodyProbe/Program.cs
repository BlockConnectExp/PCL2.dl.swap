using System;
using System.Reflection;
using System.Reflection.Emit;

namespace PclDlSwap.SwapBodyProbe
{
    internal static class Program
    {
        public static readonly int[] Observed = new int[2];

        public static int Target(int value)
        {
            Observed[0] = value;
            return value * 2;
        }

        /// <summary>Replacement body for Target(int). Signature must match exactly.</summary>
        private static int Replacement(int value)
        {
            Observed[1] = value;
            return value + 1000;
        }

        private static int Main()
        {
            var target = typeof(Program).GetMethod("Target", BindingFlags.Public | BindingFlags.Static);
            var replacement = typeof(Program).GetMethod("Replacement", BindingFlags.NonPublic | BindingFlags.Static);

            Console.WriteLine("before: Target(5)=" + Target(5) + " observed=[body:" + Observed[0] + "]");

            byte[] il = BuildCallBody(replacement, target.Module);
            Console.WriteLine("replacement IL (" + il.Length + " bytes): " + BitConverter.ToString(il).Replace("-", " "));

            try
            {
                MethodRental.SwapMethodBody(
                    typeof(Program).Module,
                    target.MetadataToken,
                    il,
                    il.Length,
                    0);
                Console.WriteLine("SwapMethodBody: returned without exception");
            }
            catch (Exception ex)
            {
                Console.WriteLine("SwapMethodBody FAILED: " + ex.GetType().Name + ": " + ex.Message);
                return 1;
            }

            int result;
            try
            {
                result = Target(7);
            }
            catch (Exception ex)
            {
                Console.WriteLine("call after swap threw: " + ex.GetType().Name + ": " + ex.Message);
                return 1;
            }

            Console.WriteLine("after:  Target(7)=" + result + " observed=[target-body:" + Observed[0] + ", replacement:" + Observed[1] + "]");
            bool ok = Observed[1] == 7 && result == 1007;
            Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
            return ok ? 0 : 1;
        }

        /// <summary>
        /// Emits a body of the shape:  call <paramref name="callee"/> ; ret
        /// The callee token is resolved in the process of the module that will own the IL.
        /// </summary>
        private static byte[] BuildCallBody(MethodInfo callee, Module targetModule)
        {
            var token = callee.MetadataToken;
            // ldarg.0 (0x02) for the first static argument, then call, then ret.
            var il = new byte[6];
            il[0] = 0x02;
            il[1] = 0x28;
            Array.Copy(BitConverter.GetBytes(token), 0, il, 2, 4);
            il[5] = 0x2A;
            return il;
        }
    }
}


<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->