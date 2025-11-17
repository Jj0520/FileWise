using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FileWise.Models;

namespace FileWise.Services;

public class ChatHistoryService
{
    private readonly string _historyFilePath;
    private ChatHistoryData _historyData;

    public ChatHistoryService()
    {
        try
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FileWise");
            
            if (!Directory.Exists(appDataPath))
            {
                try
                {
                    Directory.CreateDirectory(appDataPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error creating settings directory: {ex.Message}");
                }
            }

            _historyFilePath = Path.Combine(appDataPath, "chathistory.json");
            _historyData = LoadHistory();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing ChatHistoryService: {ex.Message}");
            _historyData = new ChatHistoryData();
            _historyFilePath = string.Empty;
        }
    }

    public List<ChatTab> GetChatTabs()
    {
        return _historyData.ChatTabs ?? new List<ChatTab>();
    }

    public void SaveChatTab(ChatTab tab)
    {
        try
        {
            if (_historyData.ChatTabs == null)
            {
                _historyData.ChatTabs = new List<ChatTab>();
            }

            var existingTab = _historyData.ChatTabs.FirstOrDefault(t => t.Id == tab.Id);
            if (existingTab != null)
            {
                existingTab.Title = tab.Title;
                existingTab.Messages = tab.Messages;
                existingTab.LastActivity = tab.LastActivity;
            }
            else
            {
                _historyData.ChatTabs.Add(tab);
            }

            SaveHistory();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving chat tab: {ex.Message}");
        }
    }

    public void DeleteChatTab(string tabId)
    {
        try
        {
            if (_historyData.ChatTabs == null)
                return;

            var tab = _historyData.ChatTabs.FirstOrDefault(t => t.Id == tabId);
            if (tab != null)
            {
                _historyData.ChatTabs.Remove(tab);
                SaveHistory();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deleting chat tab: {ex.Message}");
        }
    }

    public void ClearAllChats()
    {
        try
        {
            _historyData.ChatTabs = new List<ChatTab>();
            SaveHistory();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error clearing all chats: {ex.Message}");
        }
    }

    private ChatHistoryData LoadHistory()
    {
        try
        {
            if (File.Exists(_historyFilePath))
            {
                var json = File.ReadAllText(_historyFilePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var history = JsonSerializer.Deserialize<ChatHistoryData>(json, options);
                return history ?? new ChatHistoryData();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading chat history: {ex.Message}");
        }
        return new ChatHistoryData();
    }

    private void SaveHistory()
    {
        try
        {
            if (string.IsNullOrEmpty(_historyFilePath))
                return;
                
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true 
            };
            var json = JsonSerializer.Serialize(_historyData, options);
            File.WriteAllText(_historyFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving chat history: {ex.Message}");
        }
    }

    private class ChatHistoryData
    {
        public List<ChatTab>? ChatTabs { get; set; } = new();
    }
}

