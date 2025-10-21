using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace injector
{
    public partial class MainWindow : Window
    {
        // Constantes para inyección de DLL
        private const uint PROCESS_ALL_ACCESS = 0x001F0FFF;
        private const uint MEM_COMMIT_RESERVE = 0x00001000 | 0x00002000;
        private const uint PAGE_READWRITE = 0x04;

        private List<Process> _processes;
        private Process? _selectedProcess;
        private string? _dllPath = null;

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

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

        public MainWindow()
        {
            InitializeComponent();
            _processes = new List<Process>();
            ProcessList.SelectionChanged += ProcessList_SelectionChanged;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadProcesses();
        }

        private void ProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selectedProcessName = (string)ProcessList.SelectedItem;
            Process? process = _processes.FirstOrDefault(p => p.ProcessName == selectedProcessName);
            _selectedProcess = process;
        }

        private void LoadProcesses()
        {
            _processes = Process.GetProcesses()
                .OrderBy(p => p.ProcessName)
                .ToList();

            ProcessList.ItemsSource = _processes.Select(p => p.ProcessName);
        }


        private void ButtonInject_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProcess == null)
            {
                MessageBox.Show("Please select a process before continuing.",
                                "No Process Selected",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            if (_selectedProcess.HasExited)
            {
                MessageBox.Show("The selected process has already terminated. Please select an active process.",
                                "Process Terminated",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                LoadProcesses();
                return;
            }

            if (_dllPath == null || !File.Exists(_dllPath))
            {
                MessageBox.Show("Please select a valid DLL file before continuing.",
                                "No DLL Selected",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            try
            {
                DLLInjector(_selectedProcess, _dllPath);
                MessageBox.Show($"DLL successfully injected into {_selectedProcess.ProcessName} (PID: {_selectedProcess.Id}).",
                                "Injection Successful",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Access denied. Please run this application as Administrator.",
                                "Insufficient Permissions",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
            catch (Win32Exception ex)
            {
                MessageBox.Show($"Windows API error: {ex.Message}\nError Code: {ex.NativeErrorCode}",
                                "Injection Failed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to inject DLL: {ex.Message}",
                                "Injection Failed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        private bool ValidateDllFile(string dllPath)
        {
            try
            {
                if (!File.Exists(dllPath))
                    return false;

                if (!dllPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    return false;

                // Verificar que el archivo sea PE válido
                using (var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read))
                {
                    using (var br = new BinaryReader(fs))
                    {
                        // Leer MZ header
                        if (br.ReadUInt16() != 0x5A4D) // "MZ"
                            return false;

                        // Saltar al PE header offset
                        fs.Seek(0x3C, SeekOrigin.Begin);
                        int peOffset = br.ReadInt32();

                        // Validar PE signature
                        fs.Seek(peOffset, SeekOrigin.Begin);
                        if (br.ReadUInt32() != 0x00004550) // "PE\0\0"
                            return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool Is64BitProcess(IntPtr processHandle)
        {
            if (!Environment.Is64BitOperatingSystem)
                return false;

            IsWow64Process(processHandle, out bool isWow64);
            return !isWow64;
        }

        private bool Is64BitDll(string dllPath)
        {
            try
            {
                using (var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read))
                {
                    using (var br = new BinaryReader(fs))
                    {
                        fs.Seek(0x3C, SeekOrigin.Begin);
                        int peOffset = br.ReadInt32();
                        fs.Seek(peOffset + 4, SeekOrigin.Begin);
                        ushort machine = br.ReadUInt16();

                        // 0x8664 = AMD64, 0x014C = I386
                        return machine == 0x8664;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private void DLLInjector(Process process, string dllPath)
        {
            IntPtr processHandle = IntPtr.Zero;
            IntPtr allocMemAddress = IntPtr.Zero;
            IntPtr threadHandle = IntPtr.Zero;

            try
            {
                var buffer = Encoding.Default.GetBytes(dllPath + "\0");
                processHandle = OpenProcess(PROCESS_ALL_ACCESS, false, process.Id);

                if (processHandle == IntPtr.Zero)
                {
                    throw new Exception($"Failed to open process. Error: {Marshal.GetLastWin32Error()}");
                }

                // Validar arquitectura
                bool is64BitProc = Is64BitProcess(processHandle);
                bool is64BitDll = Is64BitDll(dllPath);

                if (is64BitProc != is64BitDll)
                {
                    throw new Exception($"Architecture mismatch: Process is {(is64BitProc ? "64-bit" : "32-bit")} but DLL is {(is64BitDll ? "64-bit" : "32-bit")}");
                }

                allocMemAddress = VirtualAllocEx(processHandle, IntPtr.Zero, (uint)buffer.Length, MEM_COMMIT_RESERVE, PAGE_READWRITE);

                if (allocMemAddress == IntPtr.Zero)
                {
                    throw new Exception($"Failed to allocate memory in remote process. Error: {Marshal.GetLastWin32Error()}");
                }

                if (!WriteProcessMemory(processHandle, allocMemAddress, buffer, (uint)buffer.Length, out _))
                {
                    throw new Exception($"Failed to write to remote process memory. Error: {Marshal.GetLastWin32Error()}");
                }

                var kernel32Handle = GetModuleHandle("kernel32.dll");
                var loadLibraryAddr = GetProcAddress(kernel32Handle, "LoadLibraryA");

                if (loadLibraryAddr == IntPtr.Zero)
                {
                    throw new Exception($"Failed to get LoadLibraryA address. Error: {Marshal.GetLastWin32Error()}");
                }

                threadHandle = CreateRemoteThread(processHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, IntPtr.Zero);

                if (threadHandle == IntPtr.Zero)
                {
                    throw new Exception($"Failed to create remote thread in target process. Error: {Marshal.GetLastWin32Error()}");
                }
            }
            finally
            {
                // Limpiar recursos
                if (threadHandle != IntPtr.Zero)
                    CloseHandle(threadHandle);

                if (processHandle != IntPtr.Zero)
                    CloseHandle(processHandle);
            }
        }

        private void ButtonRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadProcesses();
        }

        private void ButtonSelectDll_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*",
                Title = "Select DLL to Inject"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _dllPath = openFileDialog.FileName;

                if (!ValidateDllFile(_dllPath))
                {
                    MessageBox.Show("The selected file is not a valid DLL or has security issues.",
                                    "Invalid DLL",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                    _dllPath = null;
                    StatusText.Text = "Invalid DLL selected";
                }
                else
                {
                    StatusText.Text = $"Selected: {Path.GetFileName(_dllPath)}";
                }
            }
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

