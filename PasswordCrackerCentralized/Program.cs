using System;

namespace PasswordCrackerCentralized
{
    class Program
    {
        static void Main()
        {
            Cracking cracker = new Cracking();
//
//            Console.WriteLine("---> Centralized:");
//            cracker.RunCracking();

            Console.WriteLine();
            Console.WriteLine("---> Pipeline:");
            cracker.RunCrackingModified();

            Console.WriteLine();
            Console.WriteLine("Program ended successfully!");
            Console.ReadLine();
        }
    }
}
