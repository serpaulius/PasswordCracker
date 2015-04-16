﻿using System.Diagnostics;
using PasswordCrackerCentralized.model;
using PasswordCrackerCentralized.util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PasswordCrackerCentralized
{
    public class Cracking
    {
        /// <summary>
        /// The algorithm used for encryption.
        /// Must be exactly the same algorithm that was used to encrypt the passwords in the password file
        /// </summary>
        private readonly HashAlgorithm _messageDigest;

//        private const string NameOfDictionaryFile = "webster-dictionary-reduced.txt";
        private const string NameOfDictionaryFile = "webster-dictionary.txt";

        private ulong dictionaryWordCount = 0;

        public Cracking()
        {
            _messageDigest = new SHA1CryptoServiceProvider();
            //_messageDigest = new MD5CryptoServiceProvider();
            // seems to be same speed
        }

        /// <summary>
        /// Runs the password cracking algorithm
        /// </summary>
        public void RunCracking()
        {
            //Added word count
            dictionaryWordCount = 0;

            Stopwatch stopwatch = Stopwatch.StartNew();

            List<UserInfo> userInfos =
                PasswordFileHandler.ReadPasswordFile("passwords.txt");
            List<UserInfoClearText> result = new List<UserInfoClearText>();
            using (FileStream fs = new FileStream(NameOfDictionaryFile, FileMode.Open, FileAccess.Read))
            using (StreamReader dictionary = new StreamReader(fs))
            {
                while (!dictionary.EndOfStream)
                {
                    String dictionaryEntry = dictionary.ReadLine();
                    IEnumerable<UserInfoClearText> partialResult = CheckWordWithVariations(dictionaryEntry, userInfos);
                    result.AddRange(partialResult);

                    //Added counting
                    dictionaryWordCount++;
                }
            }
            stopwatch.Stop();
            Console.WriteLine(string.Join(", ", result));
            Console.WriteLine("Out of {0} password {1} was found ", userInfos.Count, result.Count);
            Console.WriteLine();
            Console.WriteLine("Time elapsed: {0}", stopwatch.Elapsed);

            //Added speed:
            Console.WriteLine("Average speed (words/second): {0}", dictionaryWordCount / (ulong)(stopwatch.Elapsed.Seconds));
        }

        public void RunCrackingModified(int encryptorCount)
        {
            dictionaryWordCount = 0;

            Stopwatch stopwatch = Stopwatch.StartNew();

            List<UserInfo> userInfos = PasswordFileHandler.ReadPasswordFile("passwords.txt");
            List<UserInfoClearText> result = new List<UserInfoClearText>();

            BlockingCollection<string> wordDictionary = new BlockingCollection<string>();

            BlockingCollection<string> transformedWordsDictionary = new BlockingCollection<string>(10000);

            //Tuple is used for joining passwords with their encrypted version, so that we will not need to search for them in other threads,
            //e.g.: password (string): "pswd", encrypted password (byte[]): "????????"
            BlockingCollection<Tuple<string, byte[]>> encryptedWordsDictionary = new BlockingCollection<Tuple<string, byte[]>>();

            //Only one cracker is veeeeeeeeeeeeery slow
            //            Parallel.Invoke(() => ReadDictionary(dictionary),
            //                () => TransformDictionary(dictionary, transformedDictionary),
            //                () => EncryptDictionary(transformedDictionary, encryptedDictionary),
            //                () => ComparePasswords(userInfos, encryptedDictionary),
            //                () => PrintCounts(dictionary, transformedDictionary, encryptedDictionary)
            //                );

            //This is a much faster solution - to make much more hash calculators

            //Making many crackers, so that we could start them in separate threads. We can't use one Cracker object,
            //because one method can only be used by one thread at the same time.
            //You can change the number of "crackers" by changing the value of howManyCrackers. It should be at least 1.
            Cracking[] crackingObjects;
            int howManyCrackers = encryptorCount;
            crackingObjects = new Cracking[howManyCrackers];
            for (int i = 0; i < howManyCrackers; i++)
            {
                crackingObjects[i] = new Cracking();
            }

            //Starting all the tasks except EncryptDictionary
            Task.Run(() => ReadDictionary(wordDictionary));
            Task.Run(() => TransformDictionary(wordDictionary, transformedWordsDictionary));
            //ComparePasswords is going to return a list of found passwords, so we are making it as a Task with return:
            Task<List<UserInfoClearText>> taskOfComparing = Task<List<UserInfoClearText>>.Factory.StartNew(
                () => ComparePasswords(userInfos, encryptedWordsDictionary)
                );

            //Run a thread for seeing how many things there are in BlockingCollections every second, while debugging:
            //Task.Run(() => PrintCounts(wordDictionary, transformedWordsDictionary, encryptedWordsDictionary));

            //The bottleneck is here (most of the calculations are made in method EncryptDictionary), so
            //we are making few processes who encrypt passwords separately.
            //So now we are running all the EncryptDictionary threads in parallel!
            Parallel.For(0, howManyCrackers,
                (i) => crackingObjects[i].EncryptDictionary(transformedWordsDictionary, encryptedWordsDictionary));
            //Can also use Tasks instead of parallel, and if you want more Encryptors and/or Comparators:
            //            for (int i = 0; i < howManyCrackers/2; i++)
            //            {
            //                int j = i;
            //                Task.Run(() => (new Cracking()).ComparePasswords(userInfos, encryptedDictionary));
            //            }
            //
            //            for (int i = 0; i < howManyCrackers / 2; i++)
            //            {
            //                int j = i;
            //                Task.Run(() => crackingMachinesHaha[j].EncryptDictionary(transformedDictionary, encryptedDictionary));
            //            }

            //Saving results (this will wait here until the ComparePasswords task in object taskOfComparing will finish it's work):
            result = taskOfComparing.Result;

            //Printing results and stopping stopwatch
            stopwatch.Stop();
            Console.WriteLine(string.Join(", ", result));
            Console.WriteLine("Out of {0} password {1} was found ", userInfos.Count, result.Count);
            Console.WriteLine();
            Console.WriteLine("Time elapsed: {0}", stopwatch.Elapsed);

            Console.WriteLine("Average speed (words/second): {0}", dictionaryWordCount/(ulong)(stopwatch.Elapsed.Seconds));
        }

        private void PrintCounts(BlockingCollection<string> one, BlockingCollection<string> two,
            BlockingCollection<Tuple<string, byte[]>> three)
        {
            while (true)
            {
                Console.WriteLine(one.Count + "\t" + two.Count + "\t" + three.Count);
                Thread.Sleep(1000);
                if (one.Count == 0 && two.Count == 0 && three.Count == 0)
                {
                    break;
                }
            }
        }

        private List<UserInfoClearText> ComparePasswords(IEnumerable<UserInfo> userInfos,
            BlockingCollection<Tuple<string, byte[]>> possiblePasswordsEncrypted)
        {
            List<UserInfoClearText> results = new List<UserInfoClearText>();

            while (!possiblePasswordsEncrypted.IsCompleted)
            {
                Tuple<string, byte[]> tuple = possiblePasswordsEncrypted.Take();
                byte[] encryptedPassword = tuple.Item2;
                string possiblePassword = tuple.Item1;
                foreach (UserInfo userInfo in userInfos)
                {
                    if (CompareBytes(userInfo.EntryptedPassword, encryptedPassword))
                    {
                        results.Add(new UserInfoClearText(userInfo.Username, possiblePassword));
                        Console.WriteLine(userInfo.Username + " " + possiblePassword);
                    }
                }
            }
            return results;
        }

        private void EncryptDictionary(BlockingCollection<string> collection,
            BlockingCollection<Tuple<string, byte[]>> encryptedCollectionInBytes)
        {
            while (!collection.IsCompleted)
            {
                string str;
                try
                {
                    str = collection.Take();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Cannot take from already completed collection!");
                    Console.WriteLine("----------------");
                    Console.WriteLine();
                    Console.WriteLine("----------------");
                    break;
                }

                char[] charArray = str.ToCharArray();
                byte[] passwordAsBytes = Array.ConvertAll(charArray, PasswordFileHandler.GetConverter());
                byte[] encryptedPassword = _messageDigest.ComputeHash(passwordAsBytes);

                try
                {
                    encryptedCollectionInBytes.Add(new Tuple<string, byte[]>(str, encryptedPassword));
                }
                catch (InvalidOperationException e)
                {
                    Console.WriteLine("All items are already Encrypted! Finishing this thread!");
                    break;
                }
            }
            encryptedCollectionInBytes.CompleteAdding();
        }

        public void ReadDictionary(BlockingCollection<string> collection)
        {
            List<UserInfoClearText> result = new List<UserInfoClearText>();
            using (FileStream fs = new FileStream(NameOfDictionaryFile, FileMode.Open, FileAccess.Read))
            using (StreamReader dictionary = new StreamReader(fs))
            {
                while (!dictionary.EndOfStream)
                {
                    String dictionaryEntry = dictionary.ReadLine();
                    collection.Add(dictionaryEntry);
                    dictionaryWordCount++;
                }
            }
            collection.CompleteAdding();
        }

        private void TransformDictionary(BlockingCollection<string> collection,
            BlockingCollection<string> transformedCollection)
        {
            Console.WriteLine("count: " + collection.Count);
            while (!collection.IsCompleted)
            {
                List<UserInfoClearText> result = new List<UserInfoClearText>();

                String possiblePassword = collection.Take();
                transformedCollection.Add(possiblePassword);

                String possiblePasswordUpperCase = possiblePassword.ToUpper();
                transformedCollection.Add(possiblePasswordUpperCase);

                String possiblePasswordCapitalized = StringUtilities.Capitalize(possiblePassword);
                transformedCollection.Add(possiblePasswordCapitalized);

                String possiblePasswordReverse = StringUtilities.Reverse(possiblePassword);
                transformedCollection.Add(possiblePasswordReverse);

                for (int i = 0; i < 100; i++)
                {
                    String possiblePasswordEndDigit = possiblePassword + i;
                    transformedCollection.Add(possiblePasswordEndDigit);
                }

                for (int i = 0; i < 100; i++)
                {
                    String possiblePasswordStartDigit = i + possiblePassword;
                    transformedCollection.Add(possiblePasswordStartDigit);
                }

                for (int i = 0; i < 10; i++)
                {
                    for (int j = 0; j < 10; j++)
                    {
                        String possiblePasswordStartEndDigit = i + possiblePassword + j;
                        transformedCollection.Add(possiblePasswordStartEndDigit);
                    }
                }
            }
            transformedCollection.CompleteAdding();
        }

        /// <summary>
        /// Generates a lot of variations, encrypts each of the and compares it to all entries in the password file
        /// </summary>
        /// <param name="dictionaryEntry">A single word from the dictionary</param>
        /// <param name="userInfos">List of (username, encrypted password) pairs from the password file</param>
        /// <returns>A list of (username, readable password) pairs. The list might be empty</returns>
        private IEnumerable<UserInfoClearText> CheckWordWithVariations(String dictionaryEntry, List<UserInfo> userInfos)
        {
            List<UserInfoClearText> result = new List<UserInfoClearText>();

            String possiblePassword = dictionaryEntry;
            IEnumerable<UserInfoClearText> partialResult = CheckSingleWord(userInfos, possiblePassword);
            result.AddRange(partialResult);

            String possiblePasswordUpperCase = dictionaryEntry.ToUpper();
            IEnumerable<UserInfoClearText> partialResultUpperCase = CheckSingleWord(userInfos, possiblePasswordUpperCase);
            result.AddRange(partialResultUpperCase);

            String possiblePasswordCapitalized = StringUtilities.Capitalize(dictionaryEntry);
            IEnumerable<UserInfoClearText> partialResultCapitalized = CheckSingleWord(userInfos,
                possiblePasswordCapitalized);
            result.AddRange(partialResultCapitalized);

            String possiblePasswordReverse = StringUtilities.Reverse(dictionaryEntry);
            IEnumerable<UserInfoClearText> partialResultReverse = CheckSingleWord(userInfos, possiblePasswordReverse);
            result.AddRange(partialResultReverse);

            for (int i = 0; i < 100; i++)
            {
                String possiblePasswordEndDigit = dictionaryEntry + i;
                IEnumerable<UserInfoClearText> partialResultEndDigit = CheckSingleWord(userInfos,
                    possiblePasswordEndDigit);
                result.AddRange(partialResultEndDigit);
            }

            for (int i = 0; i < 100; i++)
            {
                String possiblePasswordStartDigit = i + dictionaryEntry;
                IEnumerable<UserInfoClearText> partialResultStartDigit = CheckSingleWord(userInfos,
                    possiblePasswordStartDigit);
                result.AddRange(partialResultStartDigit);
            }

            for (int i = 0; i < 10; i++)
            {
                for (int j = 0; j < 10; j++)
                {
                    String possiblePasswordStartEndDigit = i + dictionaryEntry + j;
                    IEnumerable<UserInfoClearText> partialResultStartEndDigit = CheckSingleWord(userInfos,
                        possiblePasswordStartEndDigit);
                    result.AddRange(partialResultStartEndDigit);
                }
            }

            return result;
        }

        /// <summary>
        /// Checks a single word (or rather a variation of a word): Encrypts and compares to all entries in the password file
        /// </summary>
        /// <param name="userInfos"></param>
        /// <param name="possiblePassword">List of (username, encrypted password) pairs from the password file</param>
        /// <returns>A list of (username, readable password) pairs. The list might be empty</returns>
        private IEnumerable<UserInfoClearText> CheckSingleWord(IEnumerable<UserInfo> userInfos, String possiblePassword)
        {
            char[] charArray = possiblePassword.ToCharArray();
            byte[] passwordAsBytes = Array.ConvertAll(charArray, PasswordFileHandler.GetConverter());
            byte[] encryptedPassword = _messageDigest.ComputeHash(passwordAsBytes);
            //string encryptedPasswordBase64 = System.Convert.ToBase64String(encryptedPassword);

            List<UserInfoClearText> results = new List<UserInfoClearText>();
            foreach (UserInfo userInfo in userInfos)
            {
                if (CompareBytes(userInfo.EntryptedPassword, encryptedPassword))
                {
                    results.Add(new UserInfoClearText(userInfo.Username, possiblePassword));
                    Console.WriteLine(userInfo.Username + " " + possiblePassword);
                }
            }
            return results;
        }

        /// <summary>
        /// Compares to byte arrays. Encrypted words are byte arrays
        /// </summary>
        /// <param name="firstArray"></param>
        /// <param name="secondArray"></param>
        /// <returns></returns>
        private static bool CompareBytes(IList<byte> firstArray, IList<byte> secondArray)
        {
            //if (secondArray == null)
            //{
            //    throw new ArgumentNullException("firstArray");
            //}
            //if (secondArray == null)
            //{
            //    throw new ArgumentNullException("secondArray");
            //}
            if (firstArray.Count != secondArray.Count)
            {
                return false;
            }
            for (int i = 0; i < firstArray.Count; i++)
            {
                if (firstArray[i] != secondArray[i])
                    return false;
            }
            return true;
        }
    }
}