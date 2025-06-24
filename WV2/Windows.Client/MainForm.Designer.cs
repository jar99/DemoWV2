using Windows.Client.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Windows.Client;

partial class MainForm
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;
    
    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {

        webView2Control = new Browser();
        if ( _provider != null)
        {
            webView2Control = _provider.GetRequiredService<Client.Browser>();
        }
        
        
        ((System.ComponentModel.ISupportInitialize)webView2Control).BeginInit();
        SuspendLayout();
        // 
        // webView2Control
        // 
        webView2Control.AllowExternalDrop = true;
        webView2Control.BackColor = System.Drawing.SystemColors.ActiveBorder;
        webView2Control.CreationProperties = null;
        webView2Control.DefaultBackgroundColor = System.Drawing.Color.Transparent;
        webView2Control.Dock = System.Windows.Forms.DockStyle.Fill;
        webView2Control.Location = new System.Drawing.Point(0, 0);
        webView2Control.Margin = new System.Windows.Forms.Padding(2, 3, 2, 3);
        webView2Control.Name = "webView2Control";
        webView2Control.Size = new System.Drawing.Size(1317, 865);
        webView2Control.TabIndex = 1;
        webView2Control.ZoomFactor = 1D;
        // 
        // MainForm
        // 
        AutoScaleDimensions = new System.Drawing.SizeF(10F, 25F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
        ClientSize = new System.Drawing.Size(1317, 865);
        Controls.Add(webView2Control);
        Margin = new System.Windows.Forms.Padding(5, 6, 5, 6);
        Text = "Windows.Client";
        Resize += Form_Resize;
        Load += Form_Load;
        ((System.ComponentModel.ISupportInitialize)webView2Control).EndInit();
        ResumeLayout(false);
    }
    
    private Client.Browser webView2Control;

    #endregion
}