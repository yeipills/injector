using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;
using Microsoft.Win32;
using System.Linq;

namespace injector
{
    public partial class MainWindow : Window
    {
        private const string GITHUB_API_URL = "https://api.github.com/repos/YimMenu/YimMenu/releases/tags/nightly";
        private const string DLL_NAME = "YimMenu.dll";
        private List<Process> _processes;
        private Process _selectedProcess;
        private string _dllPath = null;

        // Instancia estática de HttpClient
        private static readonly HttpClient client = new ();

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        public MainWindow()
        {
            InitializeComponent();
            _processes = new List<Process>();
            ProcessList.SelectionChanged += ProcessList_SelectionChanged;

            // Inicializar HttpClient
            client.DefaultRequestHeaders.Add("User-Agent", "request");
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadProcesses();
            await DownloadLatestDll();
        }

        private void ProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selectedProcessName = (string)ProcessList.SelectedItem;
            _selectedProcess = _processes.FirstOrDefault(p => p.ProcessName == selectedProcessName);
        }

        private void LoadProcesses()
        {
            _processes = Process.GetProcesses().ToList();
            var gtaProcess = _processes.FirstOrDefault(p => p.ProcessName == "GTA5");
            if (gtaProcess != null)
            {
                _processes.Remove(gtaProcess);
                _processes.Insert(0, gtaProcess);
            }

            ProcessList.ItemsSource = _processes.Select(p => p.ProcessName);
            if (gtaProcess != null)
            {
                ProcessList.SelectedItem = "GTA5";
            }
        }

        private async Task DownloadLatestDll()
        {
            DownloadStatus.Visibility = Visibility.Visible;
            DownloadProgress.Visibility = Visibility.Visible;
            DownloadProgress.IsIndeterminate = true; // Animación de progreso indeterminado

            var json = await client.GetStringAsync(GITHUB_API_URL);
            var jobject = JObject.Parse(json);
            var latestReleaseUrl = jobject["assets"][0]["browser_download_url"].ToString();
            var bytes = await client.GetByteArrayAsync(latestReleaseUrl);
            string downloadsFolderPath = GetDownloadsFolderPath();
            string downloadFilePath = Path.Combine(downloadsFolderPath, DLL_NAME);
            File.WriteAllBytes(downloadFilePath, bytes);
            _dllPath = downloadFilePath;
            Debug.WriteLine($"Downloaded DLL to: {_dllPath}");
            if (!File.Exists(downloadFilePath))
            {
                throw new Exception("Failed to download DLL file.");
            }

            DownloadStatus.Visibility = Visibility.Collapsed;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }

        private void ButtonInject_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProcess == null)
            {
                MessageBox.Show("Please select a process before proceeding.");
                return;
            }

            if (_selectedProcess.HasExited)
            {
                MessageBox.Show("The selected process has already exited. Please select an active process.");
                return;
            }

            if (_dllPath == null || !File.Exists(_dllPath))
            {
                MessageBox.Show("Please select a valid DLL file before proceeding.");
                return;
            }

            try
            {
                DLLInjector(_selectedProcess, _dllPath);
                MessageBox.Show("DLL injected successfully!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"DLL injection failed: {ex.Message}");
            }
        }

        private void DLLInjector(Process process, string dllPath)
        {
            var buffer = System.Text.Encoding.Default.GetBytes(dllPath + "\0");
            var processHandle = OpenProcess(0x001F0FFF, false, process.Id);
            if (processHandle == IntPtr.Zero)
            {
                throw new Exception($"Failed to open the process. Error: {Marshal.GetLastWin32Error()}");
            }

            var allocMemAddress = VirtualAllocEx(processHandle, IntPtr.Zero, (uint)buffer.Length, 0x00001000 | 0x00002000, 0x04);
            if (allocMemAddress == IntPtr.Zero)
            {
                throw new Exception($"Failed to allocate memory in the remote process. Error: {Marshal.GetLastWin32Error()}");
            }

            if (!WriteProcessMemory(processHandle, allocMemAddress, buffer, (uint)buffer.Length, out _))
            {
                throw new Exception($"Failed to write to the remote process memory. Error: {Marshal.GetLastWin32Error()}");
            }

            var kernel32Handle = GetModuleHandle("kernel32.dll");
            var loadLibraryAddr = GetProcAddress(kernel32Handle, "LoadLibraryA");
            if (loadLibraryAddr == IntPtr.Zero)
            {
                throw new Exception($"Failed to get the address of LoadLibraryA. Error: {Marshal.GetLastWin32Error()}");
            }

            if (CreateRemoteThread(processHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, IntPtr.Zero) == IntPtr.Zero)
            {
                throw new Exception($"Failed to create a remote thread in the target process. Error: {Marshal.GetLastWin32Error()}");
            }
        }

        private string GetDownloadsFolderPath()
        {
            string downloadsFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return downloadsFolderPath;
        }

        private void ButtonRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadProcesses();
        }

        private void ButtonSelectDll_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "DLL Files (*.dll)|*.dll";
            if (openFileDialog.ShowDialog() == true)
            {
                _dllPath = openFileDialog.FileName;
            }
        }

        private void ProcessList_SelectionChanged_1(object sender, SelectionChangedEventArgs e)
        {
            // Este método parece estar vacío. Si no tiene funcionalidad, considera eliminarlo.
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
