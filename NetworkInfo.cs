using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows.Forms;
using System.Runtime.InteropServices;

public class NetworkInfoForm : Form
{
    private Timer updateTimer;
    private Label infoLabel;
    private NotifyIcon trayIcon;
    private ContextMenuStrip trayMenu;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x8000000;

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    private const int GWL_EXSTYLE = -20;
    private const uint LWA_ALPHA = 2;

    public NetworkInfoForm()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.BackColor = Color.Black;
        this.TransparencyKey = Color.Black;
        this.TopMost = false;
        this.ShowInTaskbar = false;
        this.ControlBox = false;

        SetWindowLong(this.Handle, GWL_EXSTYLE, GetWindowLong(this.Handle, GWL_EXSTYLE) | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
        SetLayeredWindowAttributes(this.Handle, 0, 200, LWA_ALPHA);

        // Icono en la bandeja del sistema para cerrar la app
        trayMenu = new ContextMenuStrip();
        var exitItem = new ToolStripMenuItem("Cerrar");
        exitItem.Click += (s, e) => Application.Exit();
        trayMenu.Items.Add(exitItem);

        trayIcon = new NotifyIcon();
        trayIcon.Icon = SystemIcons.Application;
        trayIcon.Text = "NetworkInfo";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;

        // Label para mostrar información
        infoLabel = new Label();
        infoLabel.AutoSize = false;
        infoLabel.ForeColor = Color.LimeGreen;
        infoLabel.BackColor = Color.Black;
        infoLabel.Font = new Font("Courier New", 10, FontStyle.Regular);
        infoLabel.Padding = new Padding(10);
        infoLabel.Top = 10;
        infoLabel.Left = 10;
        this.Controls.Add(infoLabel);

        // Configurar timer para actualizar cada 2 segundos
        updateTimer = new Timer();
        updateTimer.Interval = 2000;
        updateTimer.Tick += (s, e) => UpdateNetworkInfo();

        this.Load += (s, e) =>
        {
            SetDesktopParent();
            UpdateNetworkInfo();
            updateTimer.Start();
        };

        this.Closing += (s, e) =>
        {
            updateTimer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayMenu.Dispose();
        };

        this.Width = 600;
        this.Height = 400;
        this.StartPosition = FormStartPosition.Manual;
        PositionBottomRight();
    }

    private void UpdateNetworkInfo()
    {
        try
        {
            string info = GetNetworkInfo();
            infoLabel.Text = info;
            infoLabel.Width = this.ClientSize.Width - 20;
            infoLabel.Height = this.ClientSize.Height - 20;

            // Ajustar tamaño de la ventana según el contenido
            Size textSize = TextRenderer.MeasureText(info, infoLabel.Font);
            this.Width = Math.Min(textSize.Width + 40, Screen.PrimaryScreen.WorkingArea.Width - 20);
            this.Height = Math.Min(textSize.Height + 40, Screen.PrimaryScreen.WorkingArea.Height - 20);
            PositionBottomRight();
        }
        catch (Exception ex)
        {
            infoLabel.Text = "Error: " + ex.Message;
        }
    }

    private string GetNetworkInfo()
    {
        var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
        var activeInterfaces = new List<string>();

        foreach (NetworkInterface ni in networkInterfaces)
        {
            // Filtrar: solo Ethernet y WiFi, excluir virtuales
            if (!IsValidNetworkInterface(ni))
                continue;

            // Solo mostrar interfaces operacionales con configuración IP
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;

            var ipProperties = ni.GetIPProperties();
            var ipv4Addresses = ipProperties.UnicastAddresses
                .Where(x => x.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .ToList();

            if (ipv4Addresses.Count == 0)
                continue;

            string interfaceInfo = $"═══ {ni.Name} ═══\n";
            interfaceInfo += $"  Tipo: {GetNetworkTypeString(ni)}\n";
            interfaceInfo += $"  MAC: {ni.GetPhysicalAddress()}\n";

            foreach (var unicast in ipv4Addresses)
            {
                interfaceInfo += $"  IP: {unicast.Address}\n";
                interfaceInfo += $"  Máscara: {unicast.IPv4Mask}\n";
            }

            interfaceInfo += "\n";
            activeInterfaces.Add(interfaceInfo);
        }

        if (activeInterfaces.Count == 0)
            return "Sin interfaces de red activas.\n(Solo Ethernet y WiFi)";

        return "INFORMACIÓN DE RED ACTIVA\n" +
               DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n\n" +
               string.Join("", activeInterfaces);
    }

    private void SetDesktopParent()
    {
        IntPtr desktopHandle = FindWindow("Progman", null);
        if (desktopHandle == IntPtr.Zero)
            desktopHandle = GetDesktopWindow();

        if (desktopHandle != IntPtr.Zero)
            SetParent(this.Handle, desktopHandle);
    }

    private void PositionBottomRight()
    {
        var workingArea = Screen.PrimaryScreen.WorkingArea;
        this.Left = Math.Max(workingArea.Right - this.Width - 10, 0);
        this.Top = Math.Max(workingArea.Bottom - this.Height - 10, 0);
    }

    private bool IsValidNetworkInterface(NetworkInterface ni)
    {
        string name = ni.Name.ToLower();
        string description = ni.Description.ToLower();

        // Excluir adaptadores virtuales específicos
        string[] excludePatterns = {
            "vpn", "virtual", "hyper-v", "virtualbox", "vmware",
            "bluetooth", "loopback", "isatap", "teredo", "6to4",
            "ppp", "wan miniport", "pseudo", "ndis"
        };

        foreach (var pattern in excludePatterns)
        {
            if (name.Contains(pattern) || description.Contains(pattern))
                return false;
        }

        // Solo aceptar Ethernet y WiFi
        var validTypes = new[]
        {
            NetworkInterfaceType.Ethernet,
            NetworkInterfaceType.Wireless80211
        };

        return validTypes.Contains(ni.NetworkInterfaceType);
    }

    private string GetNetworkTypeString(NetworkInterface ni)
    {
        return ni.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Ethernet => "Ethernet",
            NetworkInterfaceType.Wireless80211 => "Wi-Fi",
            _ => ni.NetworkInterfaceType.ToString()
        };
    }

    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        Application.Run(new NetworkInfoForm());
    }
}
