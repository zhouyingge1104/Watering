using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Timers;

namespace Watering
{
    public partial class FormMain : Form
    {
        const int AREA_NUM = 33;
        const int STAT_OFF = 0, 
                     STAT_ON = 1;
        const int OP_TURN_OFF = 0,
                     OP_TURN_ON = 1;
        const int TURN_OFF_OK = 0x14,
                     TURN_ON_OK = 0x13;

        private Button[] btns;
        private TextBox[] tbxs;

        private System.Timers.Timer timer;
        private int count; //倒计时计数器

        private int[] status; //各路继电器的状态

        private Order ORDER;

        private bool firstLoad = true;

        public FormMain()
        {
            InitializeComponent();

            btns = new Button[]{
	            btn1, btn2, btn3, btn4, btn5, btn6, btn7, btn8, btn9, btn10, btn11, 
                btn12, btn13, btn14, btn15, btn16, btn17, btn18, btn19, btn20, btn21, btn22, 
                btn23, btn24, btn25, btn26, btn27, btn28, btn29, btn30, btn31, btn32, btn33
            };

            tbxs = new TextBox[]{
	            tbx1, tbx2, tbx3, tbx4, tbx5, tbx6, tbx7, tbx8, tbx9, tbx10, tbx11,
                tbx12, tbx13, tbx14, tbx15, tbx16, tbx17, tbx18, tbx19, tbx20, tbx21, tbx22,
                tbx23, tbx24, tbx25, tbx26, tbx27, tbx28, tbx29, tbx30, tbx31, tbx32, tbx33
            };

            //绑定随时保存倒计时信息的事件
            foreach (TextBox tbx in tbxs) {  tbx.Leave += new EventHandler(tbxN_Leave);  }
            //绑定33路按钮的单击事件
            foreach (Button btn in btns) {  
                btn.Click += new EventHandler(btnN_Click);
            }

            btnLG.Visible = false;
            lockAllButton();

            initTimer();

            status = new int[AREA_NUM];

            ORDER = new Order();
            ORDER.init();
        }

        private void FormMain_Load(object sender, EventArgs e)
        {
            getAppName();
            getNetConfig();
            getTimeConfig();
        }

        private void btnStatus_Click(object sender, EventArgs e)
        {
            //int a = 8;
            //MessageBox.Show(Convert.ToString(a, 2));
           
            //return;
            btnLG.Visible = false;
            queryStatus();
        }

        private void btnLG_Click(object sender, EventArgs e)
        {
            int totalTime = 0;
            byte[] order = ORDER.LG;
            //组织指令
            for (int i = 5; i < 38; i ++)
            {
                byte time = byte.Parse(tbxs[i - 5].Text);
                order[i] = time;
                totalTime += time;
            }

            TcpClient tcp = new TcpClient();
            tcp.SendTimeout = 5000;
            tcp.ReceiveTimeout = 8000;
            try
            {
                tcp.Connect(tbxIP.Text, int.Parse(tbxPort.Text));
            }
            catch (Exception ex)
            {
                lblMsg.Text = null;
                MessageBox.Show("无法连接：" + ex.Message);
                return;
            }

            if (tcp.Connected)
            {
                NetworkStream stream = tcp.GetStream();
                stream = tcp.GetStream();//创建于服务器连接的数据流
                stream.ReadTimeout = 8000;
                stream.Write(order, 0, order.Length);

                try
                {
                    byte[] resp = new byte[9];
                    stream.Read(resp, 0, resp.Length);
   
                    string str = "";
                    for (int i = 0; i < resp.Length; i++)
                    {
                        str += resp[i].ToString("x2").ToUpper() + " ";
                    }
                    Console.WriteLine();
                    Console.WriteLine("轮灌返回：" + str);

                    byte[] expectResp = { 0xFF, 0x5B, 0xFE, 0x08, 0x21, 0x01, 0x1C, 0x32, 0x00 };
                    for (int i = 0; i < resp.Length; i++ )
                    {
                        if (resp[i] != expectResp[i])
                        {
                            //MessageBox.Show("返回内容不正确，第" + (i + 1) + "位，预期 " + expectResp[i].ToString("x2").ToUpper() + "，实际 " + resp[i].ToString("x2").ToUpper());
                            lockAllButton();
                            return;
                        }
                    }

                }
                catch (Exception ex)
                {
                    stream.Dispose();
                    stream.Close();
                    tcp.Close();

                    MessageBox.Show("无法读取：" + ex.Message);
                }

                stream.Dispose();
                stream.Close();

                if (tcp != null && tcp.Connected)
                {
                    tcp.Close();
                }

                //如果之前的倒计时还没结束
                if (count > 0)
                {
                    if (totalTime * 60 > count)
                    {
                        timer.Stop();
                        count = totalTime * 60;
                        lblCountDown.Text = count + "";
                        //开始倒计时
                        timer.Start();
                    }
                }
                else
                {
                    count = totalTime * 60;
                    lblCountDown.Text = count + "";
                    //开始倒计时
                    timer.Start();
                }

            }
        }

        private void btnEarth_Click(object sender, EventArgs e)
        {
            queryEarthInfo();
            //lblEarth.Text = null;
        }

        private void tbxIP_Leave(object sender, EventArgs e)
        {
            saveNetConfig();
        }

        private void tbxPort_Leave(object sender, EventArgs e)
        {
            saveNetConfig();
        }

        /// <summary>
        /// 倒计时文本框的Leave事件
        /// </summary>
        public void tbxN_Leave(object sender, EventArgs e)
        {
            TextBox tbx = (TextBox)sender;
            try
            {
                int time = int.Parse(tbx.Text.Trim());
                if (time < 0 || time > 255) { tbx.Text = "0"; }
            }
            catch (Exception ex) { tbx.Text = "0"; }

            saveTimeConfig();
        }

        /// <summary>
        /// 33路按钮的Click事件
        /// </summary>
        public void btnN_Click(object sender, EventArgs e)
        {
            Button btn = (Button)sender;

            int index = 0;
            for(int i = 0; i < btns.Length; i ++){
                if(btn == btns[i]){
                    index = i;
                }
            }

            //发送单个指令打开或者关闭
            switch(status[index]){
                case STAT_OFF:
                    Console.WriteLine();
                    Console.WriteLine((index+1)+"区 打开命令");
                    changeRelayStatus(index, OP_TURN_ON);
                    break;

                case STAT_ON:
                    Console.WriteLine();
                    Console.WriteLine((index + 1) + "区 关闭命令");
                    changeRelayStatus(index, OP_TURN_OFF);
                    break;
            }        
        }

         /// <summary>
        /// 33路按钮的MouseDown事件
        /// </summary>
        public void btnN_MouseDown(object sender, EventArgs e)
        {
            Button btn = (Button)sender;
            btn.BackgroundImage = Image.FromFile(System.Environment.CurrentDirectory + "\\btn_gray.png"); 
        }

        /// <summary>
        /// Timer执行的事件
        /// </summary>
        public void theout(object source, System.Timers.ElapsedEventArgs e)
        {
            if (count > 0)
            {
                count --;
                this.Invoke(new EventHandler(updateCountDown));
            }
            else
            {
                if (firstLoad) //为了防止刚启动时就执行查询
                {
                    firstLoad = false;
                }
                else
                {
                    this.Invoke(new EventHandler(queryAfterCountDown));
                }
                
                timer.Stop();
            }
        }

        /// <summary>
        /// 更新倒计时
        /// </summary>
        private void updateCountDown(object sender, EventArgs e)
        {
            lblCountDown.Text = count + "";
        }

        /// <summary>
        /// 更新按钮样式
        /// </summary>
        private  void updateButton(int op, Button btn)
        {
            switch (op)
            {
                case OP_TURN_ON:
                    buttonOff(btn);
                    break;
                case OP_TURN_OFF:
                    buttonOn(btn);
                    break;
            }
        }

        /// <summary>
        /// 更新土壤湿度信息
        /// </summary>
        private void updateEarthInfo(byte[] info)
        {
            
            Console.WriteLine("update earth info");
            string msg =  "土壤湿度1：" + info[0] + "%\r\n" +
                              "土壤湿度2：" + info[1] + "%\r\n" +
                              "土壤湿度3：" + info[2] + "%\r\n";
            Console.WriteLine("msg:" + msg);
            lblEarth.Text = msg;

        }

        //工具方法区***************************************************************

        /// <summary>
        /// 获取软件名称
        /// </summary>
        private void getAppName()
        {
            string dataPath = System.Environment.CurrentDirectory;
            if (!Directory.Exists(dataPath)) { Directory.CreateDirectory(dataPath); }
            FileInfo p = new FileInfo(dataPath + "\\AppName.txt");
            if (!p.Exists) { p.Create().Close(); }
            StreamReader reader = new StreamReader(p.FullName, Encoding.Default);
            this.Text = reader.ReadLine();
            reader.Close();
        }

        /// <summary>
        /// 保存网络配置
        /// </summary>
        private void saveNetConfig(){
            string dataPath = System.Environment.CurrentDirectory;
            if (!Directory.Exists(dataPath))   {    Directory.CreateDirectory(dataPath);   }
            FileInfo p = new FileInfo(dataPath + "\\NetConfig.txt");
            StreamWriter writer = new StreamWriter(p.FullName, false);
            string config = tbxIP.Text.Trim() + "\r\n" + tbxPort.Text.Trim();
            writer.Write(config);
            writer.Close();
        }

        /// <summary>
        /// 加载网络配置
        /// </summary>
        private void getNetConfig()
        {
            string dataPath = System.Environment.CurrentDirectory;
            if (!Directory.Exists(dataPath)) { Directory.CreateDirectory(dataPath); }
            FileInfo p = new FileInfo(dataPath + "\\NetConfig.txt");
            StreamReader reader = new StreamReader(p.FullName, true);
            tbxIP.Text = reader.ReadLine();
            tbxPort.Text = reader.ReadLine();
            reader.Close();
        }

        /// <summary>
        /// 保存各区时间配置
        /// </summary>
        private void saveTimeConfig()
        {
            Console.WriteLine("Save Time...");
            string dataPath = System.Environment.CurrentDirectory;
            if (!Directory.Exists(dataPath)) { Directory.CreateDirectory(dataPath); }
            FileInfo p = new FileInfo(dataPath + "\\TimeConfig.txt");
            StreamWriter writer = new StreamWriter(p.FullName, false);

            string config = "";
            foreach(TextBox tbx in tbxs){
                if(tbx.Text.Trim().Length == 0){
                    tbx.Text = "0";
                }
                config += tbx.Text + "," ;
            }

            config = config.Substring(0, config.Length - 1);

            writer.Write(config);
            writer.Close();
        }

        /// <summary>
        /// 加载各区时间配置
        /// </summary>
        private void getTimeConfig()
        {
            string dataPath = System.Environment.CurrentDirectory;
            if (!Directory.Exists(dataPath)) { Directory.CreateDirectory(dataPath); }
            FileInfo p = new FileInfo(dataPath + "\\TimeConfig.txt");
            if (!p.Exists) { p.Create().Close(); }
            StreamReader reader = new StreamReader(p.FullName, true);
            string[] config = reader.ReadLine().Trim().Split(",".ToCharArray()); ;
            if (config.Length == AREA_NUM)
            {
                for (int i = 0; i < AREA_NUM; i++)
                {
                    tbxs[i].Text = config[i];
                }
            }
            else
            {
                MessageBox.Show("配置信息不完整");
            }

            reader.Close();
        }

        /// <summary>
        /// 倒计时结束后再次查询状态
        /// </summary>
        private void queryAfterCountDown(object sender, EventArgs e)
        {
            queryStatus();
        }

        /// <summary>
        /// 状态查询
        /// </summary>
        private void queryStatus()
        {
             TcpClient tcp = new TcpClient();
            tcp.SendTimeout = 5000;
            tcp.ReceiveTimeout = 8000;
            try
            {
                lblMsg.Text = "连接中...";
                tcp.Connect(tbxIP.Text, int.Parse(tbxPort.Text));
            }
            catch (Exception ex)
            {
                lblMsg.Text = null;
                MessageBox.Show("无法连接：" + ex.Message);
                return;
            }

            if (tcp.Connected)
            {
                lblMsg.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 已连接";
                NetworkStream stream = tcp.GetStream();
                stream = tcp.GetStream();//创建于服务器连接的数据流

                byte[] order = ORDER.QUERY_STATUS;
                stream.ReadTimeout = 8000;
                stream.Write(order, 0, order.Length);

                Console.WriteLine("Send 状态查询指令");

                try
                {
                    byte[] resp = new byte[12];
                    stream.Read(resp, 0, resp.Length);
                    Console.WriteLine("Send 读取完毕");
                    byte[] expectResp = { 0xFF, 0x5B, 0xFE, 0x08, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE6, 0x0B };

                    for (int i = 0; i < 5; i++) //比较前5位
                    {
                        if (resp[i] != expectResp[i])
                        {
                            //MessageBox.Show("返回内容不正确，第" + (i + 1) + "位，预期 " + expectResp[i].ToString("x2").ToUpper() + "，实际 " + resp[i].ToString("x2").ToUpper());
                            lockAllButton();
                            return;
                        }
                    }
                    //for test
                    
                    string str = "";
                    for (int i = 0; i < resp.Length; i++)
                    {
                        str += resp[i].ToString("x2").ToUpper() + " ";
                    }
                    //lblMsg.Text = str;
                    Console.WriteLine();
                    Console.WriteLine("查询状态返回：" + str);
                    
                    btnLG.Visible = true;
                    //从返回结果中解析各路继电器的状态
                    byte[] raw = { resp[5], resp[6], resp[7], resp[8], resp[9] };

                    getRelayStatus(raw);

                    changeButtonStatus();

                }
                catch (Exception ex)
                {
                    stream.Dispose();
                    stream.Close();
                    tcp.Close();

                    MessageBox.Show("无法读取：" + ex.Message);
                    return;
                }

                stream.Dispose();
                stream.Close();

                if (tcp != null && tcp.Connected)
                {
                    tcp.Close();
                }

            }
            else
            {
                lockAllButton();
            }
        }

        /// <summary>
        /// 单个按钮发送指令
        /// </summary>
        private void changeRelayStatus(int index, int op)
        {
            TcpClient tcp = new TcpClient();
            tcp.SendTimeout = 5000;
            tcp.ReceiveTimeout = 8000;
            try
            {
                tcp.Connect(tbxIP.Text, int.Parse(tbxPort.Text));
            }
            catch (Exception ex)
            {
                lblMsg.Text = null;
                MessageBox.Show("无法连接：" + ex.Message);
                return;
            }

            if (tcp.Connected)
            {
                NetworkStream stream = tcp.GetStream();
                stream = tcp.GetStream();//创建于服务器连接的数据流

                byte[] order = null;
                switch (op)
                {
                    case OP_TURN_ON:
                        order = ORDER.ON_SEND[index];
                        //把当前按钮对应的倒计时填入命令的最后一位
                        order[order.Length - 1] = byte.Parse(tbxs[index].Text);
                        break;
                    case OP_TURN_OFF:
                        order = ORDER.OFF_SEND[index];
                        break;
                }

                stream.ReadTimeout = 8000;
                stream.Write(order, 0, order.Length);

                try
                {
                    byte[] resp = new byte[9];
                    stream.Read(resp, 0, resp.Length);
                    Console.WriteLine("ChangeRelay 读取完毕");
                    string str = "";
                    for (int i = 0; i < resp.Length; i++)
                    {
                        str += resp[i].ToString("x2").ToUpper() + " ";
                    }
                    Console.WriteLine();
                    Console.WriteLine((index + 1) + "区 返回:" + str);

                    //判断是否打开成功或关闭成功
                    Console.WriteLine("当前操作：" + op + " 返回标志：" + resp[4]);
                    switch (op)
                    {
                        case OP_TURN_ON:
                            if (resp[4] == TURN_ON_OK)
                            {
                                status[index] = STAT_ON;
                                updateButton(op, btns[index]);
                            }
                            break;
                        case OP_TURN_OFF:
                            if (resp[4] == TURN_OFF_OK)
                            {
                                status[index] = STAT_OFF;
                                updateButton(op, btns[index]);
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    stream.Dispose();
                    stream.Close();
                    tcp.Close();

                    MessageBox.Show("无法读取：" + ex.Message);
                    return;
                }

                stream.Dispose();
                stream.Close();

                if (tcp != null && tcp.Connected)
                {
                    tcp.Close();
                }

                TextBox tbx = tbxs[index];
                int time = int.Parse(tbx.Text.Trim());

                //如果是关闭操作，则不需要考虑倒计时
                if(op == OP_TURN_OFF){
                    return;
                }

                //如果之前的倒计时还没结束
                if (count > 0)
                {
                    if (time * 60 > count)
                    {
                        timer.Stop();
                        count = time * 60;
                        lblCountDown.Text = count + "";
                        //开始倒计时
                        timer.Start();
                    }
                }
                else
                {
                    count = time * 60;
                    lblCountDown.Text = count + "";
                    //开始倒计时
                    timer.Start();
                }
            }
        }

        /// <summary>
        /// 查询土壤水分
        /// </summary>
        private void queryEarthInfo()
        {
            TcpClient tcp = new TcpClient();
            tcp.SendTimeout = 5000;
            tcp.ReceiveTimeout = 8000;
            try
            {
                lblMsg.Text = "连接中...";
                tcp.Connect(tbxIP.Text, int.Parse(tbxPort.Text));
            }
            catch (Exception ex)
            {
                lblMsg.Text = null;
                MessageBox.Show("无法连接：" + ex.Message);
                return;
            }

            if (tcp.Connected)
            {
                lblMsg.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 已连接";
                NetworkStream stream = tcp.GetStream();
                stream = tcp.GetStream();//创建于服务器连接的数据流

                byte[] order = ORDER.QUERY_EARTH_INFO;
                stream.ReadTimeout = 8000;
                stream.Write(order, 0, order.Length);
                Console.WriteLine("Send 土壤信息查询");

                try
                {
                    byte[] resp = new byte[10];
                    stream.Read(resp, 0, resp.Length);
                    Console.WriteLine("EarthInfo 读取完毕");
                    byte[] expectResp = { 0xFF, 0x5B, 0xFE, 0x0A, 0x22, 0x0B, 0x16, 0x21, 0xD5, 0x07 };

                    for (int i = 0; i < 5; i++) //比较前5位
                    {
                        if (resp[i] != expectResp[i])
                        {
                            //MessageBox.Show("返回内容不正确，第" + (i + 1) + "位，预期 " + expectResp[i].ToString("x2").ToUpper() + "，实际 " + resp[i].ToString("x2").ToUpper());
                            lockAllButton();
                            return;
                        }
                    }
                    //for test

                    string str = "";
                    for (int i = 0; i < resp.Length; i++)
                    {
                        str += resp[i].ToString("x2").ToUpper() + " ";
                    }
                    //lblMsg.Text = str;
                    Console.WriteLine();
                    Console.WriteLine("土壤信息返回：" + str);

                    btnLG.Visible = true;
                    //从返回结果中解析各路继电器的状态
                    byte[] info = new byte[] { resp[5], resp[6], resp[7] };
                    updateEarthInfo(info);

                }
                catch (Exception ex)
                {
                    stream.Dispose();
                    stream.Close();
                    tcp.Close();

                    MessageBox.Show("无法读取：" + ex.Message);
                    return;
                }

                stream.Dispose();
                stream.Close();

                if (tcp != null && tcp.Connected)
                {
                    tcp.Close();
                }

            }
            else
            {
                lockAllButton();
            }
        }

          /// <summary>
        /// 初始化Timer
        /// </summary>
        private void initTimer()
        {
            timer = new System.Timers.Timer(1000);
            timer.AutoReset = true;
            timer.Enabled = true;
            timer.Elapsed += new System.Timers.ElapsedEventHandler(theout);//到达时间的时候执行事件； 
        }

        /// <summary>
        /// 禁用某路按钮
        /// </summary>
        private void lockButton(Button btn)
        {
            btn.Enabled = false;
            btn.BackColor = Color.Gray;
            btn.ForeColor = Color.Black;
        }

        /// <summary>
        /// 禁用全部按钮
        /// </summary>
        private void lockAllButton()
        {
           foreach(Button btn in btns){
               lockButton(btn);
           }
        }

        /// <summary>
        /// 让按钮变红，准备打开继电器
        /// </summary>
        private void buttonOn(Button btn)
        {
            btn.Enabled = true;
            //btn.BackColor = Color.Crimson;
            btn.BackgroundImage = Image.FromFile(System.Environment.CurrentDirectory + "\\btn_red.png");
            btn.ForeColor = Color.White;
        }

        /// <summary>
        /// 让按钮变绿，准备关闭继电器
        /// </summary>
        private void buttonOff(Button btn)
        {
            btn.Enabled = true;
            //btn.BackColor = Color.ForestGreen;
            btn.BackgroundImage = Image.FromFile(System.Environment.CurrentDirectory + "\\btn_green.png");
            btn.ForeColor = Color.White;
        }

        /// <summary>
        /// 根据返回内容解析出各路继电器的状态
        /// </summary>
        private void getRelayStatus(byte[] raw)
        {
            Console.WriteLine("getRelayStatus  ... start");
            char[] arr = null;
            int[] res = null;

            //aw = new byte[]{ 0x01, 0x02, 0x21, 0x00, 0x00 };

            arr = raw[0].ToString("x2").ToCharArray();
            res = handleStatus(arr[1]);
            status[0] = res[3];
            status[1] = res[2];
            status[2] = res[1];
            status[3] = res[0];
            res = handleStatus(arr[0]);
            status[4] = res[3];
            status[5] = res[2];

            arr = raw[1].ToString("x2").ToCharArray();
            res = handleStatus(arr[1]);
            status[6] = res[3];
            status[7] = res[2];
            status[8] = res[1];
            status[9] = res[0];
            res = handleStatus(arr[0]);
            status[10] = res[3];
            status[11] = res[2];
            status[12] = res[1];
            status[13] = res[0];

            arr = raw[2].ToString("x2").ToCharArray();
            res = handleStatus(arr[1]);
            status[14] = res[3];
            status[15] = res[2];
            status[16] = res[1];
            status[17] = res[0];
            res = handleStatus(arr[0]);
            status[18] = res[3];
            status[19] = res[2];

            arr = raw[3].ToString("x2").ToCharArray();
            res = handleStatus(arr[1]);
            status[20] = res[3];
            status[21] = res[2];
            status[22] = res[1];
            status[23] = res[0];
            res = handleStatus(arr[0]);
            status[24] = res[3];
            status[25] = res[2];
            status[26] = res[1];
            status[27] = res[0];

            arr = raw[4].ToString("x2").ToCharArray();
            res = handleStatus(arr[1]);
            status[28] = res[3];
            status[29] = res[2];
            status[30] = res[1];
            status[31] = res[0];
            res = handleStatus(arr[0]);
            status[32] = res[3];  

            foreach(int i in status){
                Console.Write(i + " ");
            }

            Console.WriteLine("getRelayStatus  ... end");
        
        }

        /// <summary>
        /// 解析继电器状态, 返回一个4元素的int数组
        /// 比如：1 -> 0001, 2 -> 0010
        /// </summary>
        private int[] handleStatus(char aChar)
        {
            Console.WriteLine("aChar: " + aChar);

            int a = 0;
            switch(aChar){
                case 'a': a = 10; break;
                case 'b': a = 11; break;
                case 'c': a = 12; break;
                case 'd': a = 13; break;
                case 'e': a = 14; break;
                case 'f': a = 15; break;
            }
            if(a == 0){
                a = int.Parse(aChar.ToString());
            }
            

            Console.WriteLine("int a: " + a);
            string bStr = Convert.ToString(a, 2);
            if (bStr.Length < 4)
            {
                int lack = (4 - bStr.Length);
                for (int i = 0; i < lack; i++)
                {
                    bStr = "0" + bStr;
                }
            }
            char[] bArr = bStr.ToCharArray();
            int[] result = new int[4];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = int.Parse(bArr[i] + "");
            }
            return result;
        }

        /// <summary>
        /// 根据按钮状态列表决定各路按钮是否可用
        /// </summary>
        private void changeButtonStatus()
        {
            for (int i = 0; i < status.Length; i ++ )
            {
                if (status[i] == STAT_OFF)
                {
                    buttonOn(btns[i]);
                }
                else
                {
                    buttonOff(btns[i]);
                }
            }
        }

        private void btnStatus_MouseDown(object sender, MouseEventArgs e)
        {
            btnStatus.BackgroundImage = Image.FromFile(System.Environment.CurrentDirectory + "\\btn_gray.png");
        }

        private void btnStatus_MouseUp(object sender, MouseEventArgs e)
        {
            btnStatus.BackgroundImage = Image.FromFile(System.Environment.CurrentDirectory + "\\btn_red.png");
        }

        private void btnLG_MouseDown(object sender, MouseEventArgs e)
        {
            btnLG.BackgroundImage = Image.FromFile(System.Environment.CurrentDirectory + "\\btn_gray.png");
        }

        private void btnLG_MouseUp(object sender, MouseEventArgs e)
        {
            btnLG.BackgroundImage = Image.FromFile(System.Environment.CurrentDirectory + "\\btn_blue.png");
        }

        private void btnEarth_MouseDown(object sender, MouseEventArgs e)
        {
            btnEarth.BackgroundImage = Image.FromFile(System.Environment.CurrentDirectory + "\\btn_gray.png");
        }

        private void btnEarth_MouseUp(object sender, MouseEventArgs e)
        {
            btnEarth.BackgroundImage = Image.FromFile(System.Environment.CurrentDirectory + "\\btn_black.png");
        }

       

    }
       
}
