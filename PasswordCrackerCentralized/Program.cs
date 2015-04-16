using System;

namespace PasswordCrackerCentralized
{
    class Program
    {
        static void Main()
        {
            Cracking cracker = new Cracking();

            Console.WriteLine("---> Centralized:");
            cracker.RunCracking();

            Console.WriteLine();
            Console.WriteLine("---> Simple (one threaded encryption) Pipeline:");
            cracker.RunCrackingModified(1);

            Console.WriteLine();
            Console.WriteLine("---> Master/Slave'd (10-threaded encryption) Pipeline:");
            cracker.RunCrackingModified(10);

            Console.WriteLine();
            Console.WriteLine("Program ended successfully!");
            Console.ReadLine();
        }
    }
}
