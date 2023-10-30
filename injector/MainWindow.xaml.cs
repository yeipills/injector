using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        private static readonly HttpClient client = new();

        // Declaración de funciones externas desde kernel32.dll
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

            // Inicializar HttpClient con un encabezado de agente de usuario
            client.DefaultRequestHeaders.Add("User-Agent", "request");
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadProcesses(); // Cargar la lista de procesos
            await DownloadLatestDll(); // Descargar la DLL más reciente
        }

        private void ProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Manejar el cambio de selección en la lista de procesos
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
                _processes.Insert(0, gtaProcess); // Mover el proceso GTA5 al principio de la lista
            }

            ProcessList.ItemsSource = _processes.Select(p => p.ProcessName);

            if (gtaProcess != null)
            {
                ProcessList.SelectedItem = "GTA5"; // Seleccionar GTA5 si está presente
            }
        }

        private async Task DownloadLatestDll()
        {
            DownloadStatus.Visibility = Visibility.Visible;
            DownloadProgress.Visibility = Visibility.Visible;
            DownloadProgress.IsIndeterminate = true; // Configurar animación de progreso indeterminado

            var json = await client.GetStringAsync(GITHUB_API_URL); // Descargar información desde la API de GitHub
            var jobject = JObject.Parse(json);
            var latestReleaseUrl = jobject["assets"][0]["browser_download_url"].ToString(); // Obtener la URL de descarga de la última versión
            var bytes = await client.GetByteArrayAsync(latestReleaseUrl); // Descargar la DLL en bytes
            string downloadsFolderPath = GetDownloadsFolderPath();
            string downloadFilePath = Path.Combine(downloadsFolderPath, DLL_NAME);
            File.WriteAllBytes(downloadFilePath, bytes); // Guardar la DLL en el disco

            _dllPath = downloadFilePath; // Almacenar la ruta de la DLL descargada
            Debug.WriteLine($"DLL descargado en: {_dllPath}");

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
                DLLInjector(_selectedProcess, _dllPath); // Inyectar la DLL en el proceso seleccionado
                MessageBox.Show("DLL inyectado exitosamente.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fallo al inyectar la DLL: {ex.Message}");
            }
        }

        private void DLLInjector(Process process, string dllPath)
        {
            var buffer = System.Text.Encoding.Default.GetBytes(dllPath + "\0");
            var processHandle = OpenProcess(0x001F0FFF, false, process.Id); // Abrir el proceso seleccionado

            if (processHandle == IntPtr.Zero)
            {
                throw new Exception($"Fallo al abrir el proceso. Error: {Marshal.GetLastWin32Error()}");
            }

            var allocMemAddress = VirtualAllocEx(processHandle, IntPtr.Zero, (uint)buffer.Length, 0x00001000 | 0x00002000, 0x04); // Asignar memoria en el proceso remoto

            if (allocMemAddress == IntPtr.Zero)
            {
                throw new Exception($"Fallo al asignar memoria en el proceso remoto. Error: {Marshal.GetLastWin32Error()}");
            }

            if (!WriteProcessMemory(processHandle, allocMemAddress, buffer, (uint)buffer.Length, out _)) // Escribir la ruta del archivo DLL en la memoria del proceso remoto
            {
                throw new Exception($"Fallo al escribir en la memoria del proceso remoto. Error: {Marshal.GetLastWin32Error()}");
            }

            var kernel32Handle = GetModuleHandle("kernel32.dll");
            var loadLibraryAddr = GetProcAddress(kernel32Handle, "LoadLibraryA"); // Obtener la dirección de la función LoadLibraryA

            if (loadLibraryAddr == IntPtr.Zero)
            {
                throw new Exception($"Fallo al obtener la dirección de LoadLibraryA. Error: {Marshal.GetLastWin32Error()}");
            }

            if (CreateRemoteThread(processHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, IntPtr.Zero) == IntPtr.Zero) // Crear un hilo remoto para cargar la DLL
            {
                throw new Exception($"Fallo al crear un hilo remoto en el proceso objetivo. Error: {Marshal.GetLastWin32Error()}");
            }
        }

        private string GetDownloadsFolderPath()
        {
            string downloadsFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"); // Obtener la carpeta de descargas del usuario
            return downloadsFolderPath;
        }

        private void ButtonRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadProcesses(); // Actualizar la lista de procesos
        }

        private void ButtonSelectDll_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Archivos DLL (*.dll)|*.dll";

            if (openFileDialog.ShowDialog() == true)
            {
                _dllPath = openFileDialog.FileName; // Seleccionar una DLL mediante un cuadro de diálogo
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            // Iniciar la descarga del DLL más reciente cuando se haga clic en "Actualizar DLL".
            await DownloadLatestDll();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close(); // Cerrar la aplicación
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized; // Minimizar la ventana
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal; // Restaurar la ventana
            }
            else
            {
                this.WindowState = WindowState.Maximized; // Maximizar la ventana
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove(); // Mover la ventana arrastrando el título
        }
    }
}
