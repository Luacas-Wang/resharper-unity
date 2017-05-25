using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Application = UnityEngine.Application;
using Debug = UnityEngine.Debug;

namespace Plugins.Editor.JetBrains
{
  public class TTT
  {
    [Test]
    public void Test()
    {
      Console.WriteLine(WinShortcut.Parse(
        @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\JetBrains\JetBrains Rider 171.4456.242.lnk"));
    }
  }

  [InitializeOnLoad]
  public static class RiderPlugin
  {
    private static bool Initialized;
    private static string SlnFile;

    private static string DefaultApp
    {
      get
      {
        var alreadySetPath = EditorPrefs.GetString("kScriptsDefaultApp");
        if (!string.IsNullOrEmpty(alreadySetPath) && alreadySetPath.ToLower().Contains("rider") &&
            RiderPathExist(alreadySetPath))
          return alreadySetPath;

        switch (new FileInfo(alreadySetPath).Extension)
        {
          case ".lnk":
          case ".exe":
            //"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\JetBrains\*Rider*.lnk"
            //%appdata%\Microsoft\Windows\Start Menu\Programs\JetBrains Toolbox\*Rider*.lnk

            var f1 =
              new DirectoryInfo(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\JetBrains")
                .GetFiles("*Rider*.lnk");
            var f2 = new DirectoryInfo(Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
              @"Microsoft\Windows\Start Menu\Programs\JetBrains Toolbox")).GetFiles("*Rider*.lnk");
            var newPathLnk = f1.ToArray().Concat(f2).OrderBy(a => a.LastWriteTime).LastOrDefault();
            if (newPathLnk != null)
            {
              var newPath = WinShortcut.Parse(newPathLnk.FullName);
              if (EnableLogging) Debug.Log("[Rider] " + string.Format("Update {0} to {1}", alreadySetPath, newPath));
              if (!string.IsNullOrEmpty(newPath))
                EditorPrefs.SetString("kScriptsDefaultApp", newPath);
            }
            break;

          case ".app":
            // "/Applications/*Rider*.app"
            //"~/Applications/JetBrains Toolbox/*Rider*.app"
            break;

        }

        var riderPath = EditorPrefs.GetString("kScriptsDefaultApp");
        if (!RiderPathExist(riderPath))
        {
          EditorUtility.DisplayDialog("Rider executable not found",
            string.Format(@"Rider was not found in {0}{1}Please update 'External Script Editor'.", riderPath,
              Environment.NewLine), "OK");
        }

        return riderPath;
      }
    }

    public static bool TargetFrameworkVersion45
    {
      get { return EditorPrefs.GetBool("Rider_TargetFrameworkVersion45", true); }
      set { EditorPrefs.SetBool("Rider_TargetFrameworkVersion45", value); }
    }

    public static bool EnableLogging
    {
      get { return EditorPrefs.GetBool("Rider_EnableLogging", false); }
      set { EditorPrefs.SetBool("Rider_EnableLogging", value); }
    }

    public static bool Enabled
    {
      get { return EditorPrefs.GetBool("Rider_Enabled", true); }
      set { EditorPrefs.SetBool("Rider_Enabled", value); }
    }

    static RiderPlugin()
    {
      if (Enabled)
      {
        InitRiderPlugin();
      }
    }

    private static void InitRiderPlugin()
    {
      var projectDirectory = Directory.GetParent(Application.dataPath).FullName;

      var projectName = Path.GetFileName(projectDirectory);
      SlnFile = Path.Combine(projectDirectory, string.Format("{0}.sln", projectName));
      UpdateUnitySettings(SlnFile);

      InitializeEditorInstanceJson(projectDirectory);

      Initialized = true;
    }

    private static bool RiderPathExist(string path)
    {
      if (string.IsNullOrEmpty(path))
        return false;
      // windows or mac
      var fileInfo = new FileInfo(path);
      var directoryInfo = new DirectoryInfo(path);
      return fileInfo.Exists || (directoryInfo.Extension == ".app" && directoryInfo.Exists);
    }

    /// <summary>
    /// Helps to open xml and txt files at least on Windows
    /// </summary>
    /// <param name="slnFile"></param>
    private static void UpdateUnitySettings(string slnFile)
    {
      try
      {
        EditorPrefs.SetString("kScriptEditorArgs", string.Format("{0}{1}{0} {0}$(File){0}", "\"", slnFile));
      }
      catch (Exception e)
      {
        if (EnableLogging) Debug.Log("[Rider] " + ("Exception on updating kScriptEditorArgs: " + e.Message));
      }
    }

    /// <summary>
    /// Creates and deletes Library/EditorInstance.json containing version and process ID
    /// </summary>
    /// <param name="projectDirectory">Path to the project root directory</param>
    private static void InitializeEditorInstanceJson(string projectDirectory)
    {
      // Only manage EditorInstance.json for 4.x and 5.x - it's a native feature for 2017.x
#if UNITY_4 || UNITY_5
      if (EnableLogging) Debug.Log("[Rider] " + "Writing Library/EditorInstance.json");

      var library = Path.Combine(projectDirectory, "Library");
      var editorInstanceJsonPath = Path.Combine(library, "EditorInstance.json");

      File.WriteAllText(editorInstanceJsonPath, string.Format(@"{{
  ""process_id"": {0},
  ""version"": ""{1}""
}}", Process.GetCurrentProcess().Id, Application.unityVersion));

      AppDomain.CurrentDomain.DomainUnload += (sender, args) =>
      {
        if (EnableLogging) Debug.Log("[Rider] " + "Deleting Library/EditorInstance.json");
        File.Delete(editorInstanceJsonPath);
      };
#endif
    }

    /// <summary>
    /// Asset Open Callback (from Unity)
    /// </summary>
    /// <remarks>
    /// Called when Unity is about to open an asset.
    /// </remarks>
    [UnityEditor.Callbacks.OnOpenAssetAttribute()]
    static bool OnOpenedAsset(int instanceID, int line)
    {
      if (Enabled)
      {
        if (!Initialized)
        {
          // make sure the plugin was initialized first.
          // this can happen in case "Rider" was set as the default scripting app only after this plugin was imported.
          InitRiderPlugin();
          RiderAssetPostprocessor.OnGeneratedCSProjectFiles();
        }

        string appPath = Path.GetDirectoryName(Application.dataPath);

        // determine asset that has been double clicked in the project view
        var selected = EditorUtility.InstanceIDToObject(instanceID);

        if (selected.GetType().ToString() == "UnityEditor.MonoScript" ||
            selected.GetType().ToString() == "UnityEngine.Shader")
        {
          SyncSolution(); // added to handle opening file, which was just recently created.
          var assetFilePath = Path.Combine(appPath, AssetDatabase.GetAssetPath(selected));
          if (!DetectPortAndOpenFile(line, assetFilePath, new FileInfo(DefaultApp).Extension == ".exe"))
          {
            var args = string.Format("{0}{1}{0} --line {2} {0}{3}{0}", "\"", SlnFile, line, assetFilePath);
            return CallRider(args);
          }
          return true;
        }
      }
      return false;
    }


    private static bool DetectPortAndOpenFile(int line, string filePath, bool isWindows)
    {
      var startPort = 63342;
      for (int port = startPort; port < startPort + 21; port++)
      {
        var aboutUrl = string.Format("http://localhost:{0}/api/about/", port);
        var aboutUri = new Uri(aboutUrl);

        using (var client = new WebClient())
        {
          client.Headers.Add("origin", string.Format("http://localhost:{0}", port));
          client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";

          var responce = CallHttpApi(port, aboutUri, client);
          if (responce.ToLower().Contains("rider"))
          {
            return HttpOpenFile(line, filePath, isWindows, port, client);
          }
        }
      }
      return false;
    }

    private static bool HttpOpenFile(int line, string filePath, bool isWindows, int port, WebClient client)
    {
      var url = string.Format("http://localhost:{0}/api/file?file={1}{2}", port, filePath,
        line < 0
          ? "&p=0"
          : "&line=" + line); // &p is needed to workaround https://youtrack.jetbrains.com/issue/IDEA-172350
      if (isWindows)
        url = string.Format(@"http://localhost:{0}/api/file/{1}{2}", port, filePath, line < 0 ? "" : ":" + line);

      var uri = new Uri(url);
      if (EnableLogging) Debug.Log("[Rider] " + string.Format("HttpRequestOpenFile({0})", uri.AbsoluteUri));

      try
      {
        CallHttpApi(port, uri, client);
      }
      catch (Exception e)
      {
        Debug.Log("[Rider] " + "Exception in HttpRequestOpenFile: " + e);
        return false;
      }
      ActivateWindow(new FileInfo(DefaultApp).FullName);
      return true;
    }

    private static string CallHttpApi(int port, Uri uri, WebClient client)
    {
      var responseString = client.DownloadString(uri);
      if (EnableLogging) Debug.Log("[Rider] HttpRequestOpenFile response: " + responseString);
      return responseString;
    }

    private static bool CallRider(string args)
    {
      var riderFileInfo = new FileInfo(DefaultApp);
      var macOSVersion = riderFileInfo.Extension == ".app";
      if (!RiderPathExist(riderFileInfo.FullName))
      {
        return false;
      }

      var proc = new Process();
      if (macOSVersion)
      {
        proc.StartInfo.FileName = "open";
        proc.StartInfo.Arguments = string.Format("-n {0}{1}{0} --args {2}", "\"", "/" + DefaultApp, args);
        if (EnableLogging) Debug.Log("[Rider] " + proc.StartInfo.FileName + " " + proc.StartInfo.Arguments);
      }
      else
      {
        proc.StartInfo.FileName = DefaultApp;
        proc.StartInfo.Arguments = args;
        if (EnableLogging)
          Debug.Log("[Rider] " + ("\"" + proc.StartInfo.FileName + "\"" + " " + proc.StartInfo.Arguments));
      }

      proc.StartInfo.UseShellExecute = false;
      proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
      proc.StartInfo.CreateNoWindow = true;
      proc.StartInfo.RedirectStandardOutput = true;
      proc.Start();

      ActivateWindow(DefaultApp);
      return true;
    }

    private static void ActivateWindow(string riderPath)
    {
      if (new FileInfo(riderPath).Extension == ".exe")
      {
        try
        {
          var process = Process.GetProcesses().FirstOrDefault(p =>
          {
            string processName;
            try
            {
              processName =
                p.ProcessName; // some processes like kaspersky antivirus throw exception on attempt to get ProcessName
            }
            catch (Exception)
            {
              return false;
            }

            return !p.HasExited && processName.ToLower().Contains("rider");
          });
          if (process != null)
          {
            // Collect top level windows
            var topLevelWindows = User32Dll.GetTopLevelWindowHandles();
            // Get process main window title
            var windowHandle = topLevelWindows.FirstOrDefault(hwnd => User32Dll.GetWindowProcessId(hwnd) == process.Id);
            Debug.Log("[Rider] ActivateWindow: " + process.Id + " " + windowHandle);
            if (windowHandle != IntPtr.Zero)
            {
              //User32Dll.ShowWindow(windowHandle, 9); //SW_RESTORE = 9
              User32Dll.SetForegroundWindow(windowHandle);
            }
          }
        }
        catch (Exception e)
        {
          Debug.Log("[Rider] " + ("Exception on ActivateWindow: " + e));
        }
      }
    }

    [MenuItem("Assets/Open C# Project in Rider", false, 1000)]
    static void MenuOpenProject()
    {
      // Force the project files to be sync
      SyncSolution();

      // Load Project
      CallRider(string.Format("{0}{1}{0}", "\"", SlnFile));
    }

    [MenuItem("Assets/Open C# Project in Rider", true, 1000)]
    static bool ValidateMenuOpenProject()
    {
      return Enabled;
    }

    /// <summary>
    /// Force Unity To Write Project File
    /// </summary>
    private static void SyncSolution()
    {
      System.Type T = System.Type.GetType("UnityEditor.SyncVS,UnityEditor");
      System.Reflection.MethodInfo SyncSolution = T.GetMethod("SyncSolution",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
      SyncSolution.Invoke(null, null);
    }

    /// <summary>
    /// JetBrains Rider Integration Preferences Item
    /// </summary>
    /// <remarks>
    /// Contains all 3 toggles: Enable/Disable; Debug On/Off; Writing Launch File On/Off
    /// </remarks>
    [PreferenceItem("Rider")]
    static void RiderPreferencesItem()
    {
      EditorGUILayout.BeginVertical();

      var url = "https://github.com/JetBrains/Unity3dRider";
      if (GUILayout.Button(url))
      {
        Application.OpenURL(url);
      }

      EditorGUI.BeginChangeCheck();

      var enabledMsg = @"Enable Unity3dRider.";
      Enabled =
        EditorGUILayout.Toggle(
          new GUIContent("Enable Unity3dRider",
            enabledMsg), Enabled);
      EditorGUILayout.HelpBox(enabledMsg, MessageType.None);

      var help = @"For now target 4.5 is strongly recommended.
 - Without 4.5:
    - Rider will fail to resolve System.Linq on Mac/Linux
    - Rider will fail to resolve Firebase Analytics.
 - With 4.5 Rider will show ambiguous references in UniRx.
All those problems will go away after Unity upgrades to mono4.";
      TargetFrameworkVersion45 =
        EditorGUILayout.Toggle(
          new GUIContent("TargetFrameworkVersion 4.5",
            help), TargetFrameworkVersion45);
      EditorGUILayout.HelpBox(help, MessageType.None);

      EditorGUI.EndChangeCheck();

      EditorGUI.BeginChangeCheck();

      var loggingMsg =
        @"Enable logging. If you are about to report an issue, please enable logging and attach Unity console output to the issue.";
      EnableLogging =
        EditorGUILayout.Toggle(
          new GUIContent("Enable Logging",
            loggingMsg), EnableLogging);
      EditorGUILayout.HelpBox(loggingMsg, MessageType.None);

      EditorGUI.EndChangeCheck();
    }

    static class User32Dll
    {

      /// <summary>
      /// Gets the ID of the process that owns the window.
      /// Note that creating a <see cref="Process"/> wrapper for that is very expensive because it causes an enumeration of all the system processes to happen.
      /// </summary>
      public static int GetWindowProcessId(IntPtr hwnd)
      {
        uint dwProcessId;
        GetWindowThreadProcessId(hwnd, out dwProcessId);
        return unchecked((int) dwProcessId);
      }

      /// <summary>
      /// Lists the handles of all the top-level windows currently available in the system.
      /// </summary>
      public static List<IntPtr> GetTopLevelWindowHandles()
      {
        var retval = new List<IntPtr>();
        EnumWindowsProc callback = (hwnd, param) =>
        {
          retval.Add(hwnd);
          return 1;
        };
        EnumWindows(Marshal.GetFunctionPointerForDelegate(callback), IntPtr.Zero);
        GC.KeepAlive(callback);
        return retval;
      }

      public delegate Int32 EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

      [DllImport("user32.dll", CharSet = CharSet.Unicode, PreserveSig = true, SetLastError = true,
        ExactSpelling = true)]
      public static extern Int32 EnumWindows(IntPtr lpEnumFunc, IntPtr lParam);

      [DllImport("user32.dll", SetLastError = true)]
      static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

      [DllImport("user32.dll", CharSet = CharSet.Unicode, PreserveSig = true, SetLastError = true,
        ExactSpelling = true)]
      public static extern Int32 SetForegroundWindow(IntPtr hWnd);

      [DllImport("user32.dll", CharSet = CharSet.Unicode, PreserveSig = true, SetLastError = true,
        ExactSpelling = true)]
      public static extern UInt32 ShowWindow(IntPtr hWnd, Int32 nCmdShow);
    }
  }

  public static class WinShortcut
  {
    public static string Parse(String shortcutPath)
    {
      using (var fs = new FileStream(shortcutPath, FileMode.Open, FileAccess.Read))
      {
      //LocalBasePath: (14 bytes, offset 0x0138), because VolumeIDAndLocalBasePath is set, the
      //character string "c:\test\a.txt" is present.
      
        fs.Seek(0x0138, SeekOrigin.Begin);
        var data = new byte[14];
        fs.Read(data, 0, data.Length);
        var localBasePath = Encoding.Default.GetString(data);

        return localBasePath;
      }
    }
  }
}

// Developed using JetBrains Rider =)
