﻿using CN_GreenLumaGUI.Messages;
using CN_GreenLumaGUI.Models;
using CN_GreenLumaGUI.Pages;
using CN_GreenLumaGUI.tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace CN_GreenLumaGUI.ViewModels
{
	public class GameListPageViewModel : ObservableObject
	{
		readonly GameListPage page;
		public GameListPageViewModel(GameListPage page, ObservableCollection<GameObj> gamesList)
		{
			this.page = page;
			this.gamesList = gamesList;
			DataSystem.Instance.LoadData();
			WeakReferenceMessenger.Default.Send(new CheckedNumChangedMessage(0, false));

			WeakReferenceMessenger.Default.Register<GameListChangedMessage>(this, (r, m) =>
			{
				OnPropertyChanged(nameof(PageEndText));
			});

            // 启动客户端时尝试清理
            try
            {
                if (File.Exists(GLFileTools.DLLInjectorExePath))
                    File.Delete(GLFileTools.DLLInjectorExePath);
                if (File.Exists(GLFileTools.DLLInjectorExeBakPath))
                    File.Delete(GLFileTools.DLLInjectorExeBakPath);
            }
            catch { }
            for (int i = 0; i < 10; i++)
            {
                var dllFileName = $"{GLFileTools.DLLInjectorConfigDir}\\GreenLuma{i}.dll";
                try
                {
                    if (File.Exists(dllFileName))
                        File.Delete(dllFileName);
                }
                catch { }
            }

            // 更新时尝试清除缓存
            if (DataSystem.Instance.LastVersion != "null" && DataSystem.Instance.LastVersion != Program.Version)
			{
				try
                {
                    if (Directory.Exists(GLFileTools.DLLInjectorConfigDir))
                        Directory.Delete(GLFileTools.DLLInjectorConfigDir, true);
                    DataSystem.Instance.LastVersion = Program.Version;
				}
				catch
				{
					MessageBox.Show("Because the file is occupied, this update can not take effect, may occur the game can not be properly unlocked. \nThis problem can be resolved by rebooting the computer or shutting down the Steam completely.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
				}
			}
		}
		//Cmd



		//Binding

		private ObservableCollection<GameObj> gamesList;

		public ObservableCollection<GameObj> GamesList
		{
			get { return gamesList; }
			set
			{
				gamesList = value;
				OnPropertyChanged();
			}
		}

		public string PageEndText
		{
			get
			{
				int count = DataSystem.Instance.GetGameDatas().Count;
				if (count == 0)
					return "There are no games available yet. You can add a few through the search interface.";
				if (count > 5)
					return "No more games available...";
				return "";
			}
		}


	}
}
