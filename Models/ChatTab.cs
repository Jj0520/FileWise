using System;
using System.Collections.Generic;

namespace FileWise.Models;

public class ChatTab
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New Chat";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastActivity { get; set; } = DateTime.Now;
    private List<ChatMessage>? _messages;
    public List<ChatMessage> Messages 
    { 
        get => _messages ??= new List<ChatMessage>();
        set => _messages = value;
    }
}

