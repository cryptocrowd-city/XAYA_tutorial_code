﻿using BitcoinLib.Services.Coins.XAYA;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace XAYAWrapper
{
    static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr LoadLibrary(string dllToLoad);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

        [DllImport("kernel32.dll")]
        public static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll")]
        public static extern bool SetDllDirectory(string pathName);

        [DllImport("kernel32.dll")]
        public static extern UInt32 WaitForSingleObject(IntPtr hHandle, UInt32 dwMilliseconds);
    }


    enum ChainType
    {
        MAIN,
        TEST,
        REGTEST
    }


    public class XayaWrapper
    {
        public delegate string InitialCallback(out int height, out string hashHex);
        public delegate string ForwardCallback(string oldState, string blockData, string undoData, out string newData);
        public delegate string BackwardCallback(string newState, string blockData, string undoData);

        InitialCallback initialCallback;
        ForwardCallback forwardCallback;
        BackwardCallback backwardsCallback;

        IntPtr pDll;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void setInitialCallback(InitialCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void setForwardCallback(ForwardCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void setBackwardCallback(BackwardCallback callback);
        
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CSharp_ConnectToTheDaemon(string gameId, string XayaRpcUrl, int GameRpcPort, int EnablePruning, int chain, string storageType, string dataDirectory, string glogName, string glogDataDir);

        public IXAYAService xayaGameService;


        public XayaWrapper(string dataPath, string host_s, string gamehostport_s,  ref string result, InitialCallback inCal, ForwardCallback forCal, BackwardCallback backCal)
        {
            if(!NativeMethods.SetDllDirectory(dataPath + "\\..\\XayaStateProcessor\\"))
            {
                result = "Could not set dll directory";
                return;
            }

            string path = dataPath.Replace("/", "\\") + "\\..\\XayaStateProcessor\\libxayawrap.dll";

            pDll = NativeMethods.LoadLibrary(dataPath.Replace("/","\\") + "\\..\\XayaStateProcessor\\libxayawrap.dll");
            //Console.WriteLine("error: " + Marshal.GetLastWin32Error());

            if (pDll == IntPtr.Zero)
            {
                result = "Could not load " + dataPath.Replace("/", "\\") + "\\..\\XayaStateProcessor\\libxayawrap.dll";
                return;
            }

            IntPtr pSetInitialCallback = NativeMethods.GetProcAddress(pDll, "setInitialCallback");   
            if(pSetInitialCallback == IntPtr.Zero)
            {
                result = "Could not load resolve setInitialCallback";
                return;
            }

            IntPtr pSetForwardCallback = NativeMethods.GetProcAddress(pDll, "setForwardCallback");
            if (pSetForwardCallback == IntPtr.Zero)
            {
                result = "Could not load resolve pSetForwardCallback";
                return;
            }

            IntPtr pSetBackwardCallback = NativeMethods.GetProcAddress(pDll, "setBackwardCallback");
            if (pSetBackwardCallback == IntPtr.Zero)
            {
                result = "Could not load resolve pSetBackwardCallback";
                return;
            }

            setInitialCallback SetInitialCallback = (setInitialCallback)Marshal.GetDelegateForFunctionPointer(pSetInitialCallback, typeof(setInitialCallback));
            setForwardCallback SetForwardlCallback = (setForwardCallback)Marshal.GetDelegateForFunctionPointer(pSetForwardCallback, typeof(setForwardCallback));
            setBackwardCallback SetBackwardCallback = (setBackwardCallback)Marshal.GetDelegateForFunctionPointer(pSetBackwardCallback, typeof(setBackwardCallback));

            initialCallback = new InitialCallback(inCal);
            SetInitialCallback(initialCallback);

            forwardCallback = new ForwardCallback(forCal);
            SetForwardlCallback(forwardCallback);

            backwardsCallback = new BackwardCallback(backCal);
            SetBackwardCallback(backwardsCallback);
           
            xayaGameService = new XAYAService(host_s + ":" + gamehostport_s, "","","");
            IsConnected = true;
            result = "Wrapper Initialised";
        }

        public bool IsConnected = false;

        public string Connect(string dataPath, string FLAGS_xaya_rpc_url, string gamehostport_s, string chain_s, string storage_s, string gamenamespace, string databasePath, string glogsPath)
        {
            IntPtr pDaemonConnect = NativeMethods.GetProcAddress(pDll, "CSharp_ConnectToTheDaemon");
            if (pDaemonConnect == IntPtr.Zero)
            {
                return "Could not load resolve CSharp_ConnectToTheDaemon";
            }

            CSharp_ConnectToTheDaemon ConnectToTheDaemon_CSharp = (CSharp_ConnectToTheDaemon)Marshal.GetDelegateForFunctionPointer(pDaemonConnect, typeof(CSharp_ConnectToTheDaemon));

            if (!Directory.Exists(glogsPath))
            {
                Directory.CreateDirectory(glogsPath);
            }

            // Storage type can be: "memory", "lmdb", or "sqlite".
            // For types other than memory, dataDirectory must be set.
            try
            {
                FLAGS_xaya_rpc_url = FLAGS_xaya_rpc_url.Replace("http://", ""); // not sure why, but curl in xayalib dislikes http prefix
                ConnectToTheDaemon_CSharp(gamenamespace, FLAGS_xaya_rpc_url, int.Parse(gamehostport_s), -1, int.Parse(chain_s), storage_s, databasePath, "XayaGLOG", glogsPath);
            }
            catch (ThreadAbortException)
            {
                Thread.ResetAbort();
            }

            // This we be blocked by the DLL until the "Stop" command is issued.
            ShutdownDaemon();
            return "Done: Wrapper connected.";
        }


        public void Stop()
        {        
            xayaGameService.Stop();
            Console.Write("Stop command issued.");
        }


        public void ShutdownDaemon()
        {
            if (pDll != IntPtr.Zero)
            {
                initialCallback = null;
                forwardCallback = null;
                backwardsCallback = null;
                NativeMethods.FreeLibrary(pDll);
                pDll = IntPtr.Zero;
            }
        }
    }
}
