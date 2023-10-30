using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace injector
{
    public partial class MainWindow : Window
    {
        private const string GITHUB_API_URL = "https://api.github.com/repos/YimMenu/YimMenu/releases/tags/nightly";
        private const string DLL_NAME = "YimMenu.dll";

        // Constantes para inyección de DLL
        private const uint PROCESS_ALL_ACCESS = 0x001F0FFF;
        private const uint MEM_COMMIT_RESERVE = 0x00001000 | 0x00002000;
        private const uint PAGE_READWRITE = 0x04;

        private List<Process> _processes;
        private Process _selectedProcess;
        private string _dllPath = null;

        // Instancia estática de HttpClient
        private static readonly HttpClient client = new HttpClient { DefaultRequestHeaders = { { "User-Agent", "request" } } };

        // Declaraciones de funciones externas desde kernel32.dll
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
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadProcesses();
            await DownloadLatestDll();
        }

        private void ProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selectedProcessName = (string)ProcessList.SelectedItem;
            Process? process = _processes.FirstOrDefault(p => p.ProcessName == selectedProcessName);
            _selectedProcess = process;
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
            DownloadProgress.IsIndeterminate = true;

            var json = await client.GetStringAsync(GITHUB_API_URL);
            var jobject = JObject.Parse(json);
            var latestReleaseUrl = jobject["assets"][0]["browser_download_url"].ToString();
            var bytes = await client.GetByteArrayAsync(latestReleaseUrl);
            string downloadsFolderPath = GetDownloadsFolderPath();
            string downloadFilePath = Path.Combine(downloadsFolderPath, DLL_NAME);
            File.WriteAllBytes(downloadFilePath, bytes);

            _dllPath = downloadFilePath;

            if (!File.Exists(downloadFilePath))
            {
                throw new Exception("Error al descargar el archivo DLL.");
            }

            DownloadStatus.Visibility = Visibility.Collapsed;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }

        private void ButtonInject_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProcess == null)
            {
                MessageBox.Show("Por favor, selecciona un proceso antes de continuar.");
                return;
            }

            if (_selectedProcess.HasExited)
            {
                MessageBox.Show("El proceso seleccionado ya ha finalizado. Por favor, selecciona un proceso activo.");
                return;
            }

            if (_dllPath == null || !File.Exists(_dllPath))
            {
                MessageBox.Show("Por favor, selecciona un archivo DLL válido antes de continuar.");
                return;
            }

            try
            {
                DLLInjector(_selectedProcess, _dllPath);
                MessageBox.Show("DLL inyectado exitosamente.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fallo al inyectar la DLL: {ex.Message}");
            }
        }

        private void DLLInjector(Process process, string dllPath)
        {
            var buffer = Encoding.Default.GetBytes(dllPath + "\0");
            var processHandle = OpenProcess(PROCESS_ALL_ACCESS, false, process.Id);

            if (processHandle == IntPtr.Zero)
            {
                throw new Exception($"Fallo al abrir el proceso. Error: {Marshal.GetLastWin32Error()}");
            }

            var allocMemAddress = VirtualAllocEx(processHandle, IntPtr.Zero, (uint)buffer.Length, MEM_COMMIT_RESERVE, PAGE_READWRITE);

            if (allocMemAddress == IntPtr.Zero)
            {
                throw new Exception($"Fallo al asignar memoria en el proceso remoto. Error: {Marshal.GetLastWin32Error()}");
            }

            if (!WriteProcessMemory(processHandle, allocMemAddress, buffer, (uint)buffer.Length, out _))
            {
                throw new Exception($"Fallo al escribir en la memoria del proceso remoto. Error: {Marshal.GetLastWin32Error()}");
            }

            var kernel32Handle = GetModuleHandle("kernel32.dll");
            var loadLibraryAddr = GetProcAddress(kernel32Handle, "LoadLibraryA");

            if (loadLibraryAddr == IntPtr.Zero)
            {
                throw new Exception($"Fallo al obtener la dirección de LoadLibraryA. Error: {Marshal.GetLastWin32Error()}");
            }

            if (CreateRemoteThread(processHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, IntPtr.Zero) == IntPtr.Zero)
            {
                throw new Exception($"Fallo al crear un hilo remoto en el proceso objetivo. Error: {Marshal.GetLastWin32Error()}");
            }
        }

        private string GetDownloadsFolderPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        private void ButtonRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadProcesses();
        }

        private void ButtonSelectDll_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Archivos DLL (*.dll)|*.dll"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _dllPath = openFileDialog.FileName;
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            await DownloadLatestDll();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }
    }
}

