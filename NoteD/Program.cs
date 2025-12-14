using Terminal.Gui;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using NStack;


Application.Init();

string? notesFolder = null;
string? currentFilePath = null;

var noteFiles = new ObservableCollection<string>();

var isNoteDirty = false;
Timer? autoSaveTimer = null;

SettingsManager.LoadSettings();

// Will use this line for later customization of accent color
// Colors.Base.Normal = Application.Driver.MakeAttribute(Color.Green, Color.Black);

var listView = new ListView
{
    X = 0,
    Y = 0,
    Width = 40,
    Height = Dim.Fill(),
    CanFocus = true
};

var textView = new TextView
{
    X = Pos.Right(listView) + 1,
    Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill(),
    AllowsTab = true,
    WordWrap = true,
    CanFocus = true
};

textView.KeyPress += (key) =>
{
    if (key.KeyEvent.Key >= Key.Space || 
        key.KeyEvent.Key == Key.Backspace || 
        key.KeyEvent.Key == Key.Delete || 
        key.KeyEvent.Key == Key.Enter)
    {
        isNoteDirty = true;
    }
    key.Handled = false; 
};

notesFolder = LoadOrChooseNotesFolder();

var win = new Window
{
    Title = "NoteD - The Simple Note Taker",
    X = 0,
    Y = 1,
    Width = Dim.Fill(),
    Height = Dim.Fill()
};

var menu = new MenuBar
{
    Menus =
    [
        new MenuBarItem("_File",
        [
            new MenuItem("_Choose Notes Folder", "", ChooseNotesFolder),
            new MenuItem("_New Note", "", CreateNewNote),
            new MenuItem("_Save Current Note", "", SaveCurrentNote),
            new MenuItem("_Delete Selected Note", "", DeleteSelectedNote),
            new MenuItem("_Quit", "", () => Application.RequestStop())
        ]),
        new MenuBarItem("_Settings",
        [
            new MenuItem("_Open Settings File", "", () =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SettingsManager.SettingsFilePath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Could not open settings file:\n{ex.Message}", "OK");
                }
            })
        ])
    ]
};

win.Add(listView, textView);

Application.Top.Add(menu, win);

if (SettingsManager.Settings.AutoSaveEnabled)
{
    autoSaveTimer = new Timer(
        callback: (_) => Application.MainLoop.Invoke(SaveNoteIfDirty),
        state: null,
        dueTime: SettingsManager.Settings.AutoSaveDelayMs, 
        period: SettingsManager.Settings.AutoSaveDelayMs 
    );
}

RefreshNoteList();

listView.SelectedItemChanged += (args) =>
{
    var sel = args.Item;
    if (noteFiles.Count == 0 || sel < 0 || sel >= noteFiles.Count) return;

    if (isNoteDirty)
    {
        SaveNoteIfDirty(); 
    }

    var selectedFile = Path.Combine(notesFolder!, noteFiles[sel]);

    if (File.Exists(selectedFile))
    {
        textView.Text = File.ReadAllText(selectedFile);
        currentFilePath = selectedFile;
        isNoteDirty = false;
    }
};

void RefreshNoteList()
{
    if (!Directory.Exists(notesFolder))
    {
        noteFiles.Clear();
        listView.SetSource(noteFiles);
        textView.Text = "";
        currentFilePath = null;
        return;
    }

    if (notesFolder == null)
    {
        noteFiles.Clear();
        listView.SetSource(noteFiles);
        return;
    }

    var files = Directory.GetFiles(notesFolder, "*.md")
        .Select(Path.GetFileName)
        .Cast<string>()
        .OrderByDescending(f => f)
        .ToList();
    
    
    noteFiles.Clear();
    foreach (var f in files) noteFiles.Add(f);
    listView.SetSource(noteFiles);

    if (noteFiles.Count > 0 && (listView.SelectedItem < 0 || listView.SelectedItem >= noteFiles.Count))
    {
        listView.SelectedItem = 0;
    }
}

void ChooseNotesFolder()
{
    var examplePath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\Users\You\Documents\NoteD" : "/home/you/Documents/NoteD";
    
    var inputDialog = new Dialog
    {
        Title = "Choose Notes Folder",
        Width = 60,
        Height = 10
    };

    var label = new Label
    {
        Text = $"Enter full path to notes folder (e.g. {examplePath}):",
        X = 1,
        Y = 1,
        Width = Dim.Fill() - 2
    };

    var textField = new TextField
    {
        X = 1,
        Y = 3,
        Width = Dim.Fill() - 2
    };

    var okButton = new Button
    {
        Text = "OK",
        IsDefault = true
    };

    okButton.Clicked += () =>
    {
        var path = (textField.Text ?? ustring.Empty).ToString()!.Trim(); 
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                Directory.CreateDirectory(path);
                notesFolder = path;
                RefreshNoteList();
                MessageBox.Query("Success", $"Notes folder set to:\n{notesFolder}", "OK");
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Invalid path:\n{ex.Message}", "OK");
            }
        }
        Application.RequestStop();
    };

    var cancelButton = new Button { Text = "Cancel" };
    cancelButton.Clicked += () => Application.RequestStop();

    inputDialog.Add(label, textField);
    inputDialog.AddButton(okButton);
    inputDialog.AddButton(cancelButton);

    Application.Run(inputDialog);
}

string LoadOrChooseNotesFolder()
{
    var defaultFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "NoteD"
    );

    if (Directory.Exists(defaultFolder))
        return defaultFolder;

    ChooseNotesFolder();
    return notesFolder ?? throw new Exception("No folder selected");
}

void CreateNewNote()
{
    if (string.IsNullOrEmpty(notesFolder))
    {
        MessageBox.ErrorQuery("Error", "No notes folder selected yet.", "OK");
        return;
    }

    var nameDialog = new Dialog
    {
        Title = "New Note",
        Width = 50,
        Height = 10
    };

    var label = new Label
    {
        Text = "Optional name (leave blank for just date):",
        X = 1,
        Y = 1
    };

    var nameField = new TextField
    {
        X = 1,
        Y = 3,
        Width = Dim.Fill() - 2
    };

    var ok = new Button
    {
        Text = "Create",
        IsDefault = true
    };

    ok.Clicked += () =>
    {
        var optional = (nameField.Text ?? ustring.Empty).ToString()!.Trim(); 
        var date = DateTime.Today.ToString("yyyy-MM-dd");
        var slug = string.IsNullOrWhiteSpace(optional) ? "" : $"-{optional}";
        var filename = $"{date}{slug}.md";
        var fullPath = Path.Combine(notesFolder!, filename);

        int counter = 1;
        while (File.Exists(fullPath))
        {
            filename = $"{date}{slug}-{counter++}.md";
            fullPath = Path.Combine(notesFolder!, filename);
        }

        File.WriteAllText(fullPath, "");
        RefreshNoteList();

        var index = noteFiles.IndexOf(filename);
        if (index >= 0)
        {
            listView.SelectedItem = index;
            textView.Text = "";
            currentFilePath = fullPath;
            isNoteDirty = false;
            textView.SetFocus();
        }

        Application.RequestStop();
    };

    nameDialog.Add(label, nameField);
    nameDialog.AddButton(ok);

    Application.Run(nameDialog);
}

void SaveNoteIfDirty()
{
    if (isNoteDirty && !string.IsNullOrEmpty(currentFilePath) && !string.IsNullOrEmpty(notesFolder))
    {
        try
        {
            File.WriteAllText(currentFilePath, textView.Text!.ToString());
            isNoteDirty = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Auto-Save Failed: {ex.Message}");
        }
    }
}

void SaveCurrentNote()
{
    if (string.IsNullOrEmpty(currentFilePath) || string.IsNullOrEmpty(notesFolder))
    {
        MessageBox.ErrorQuery("Error", "No note open to save.", "OK");
        return;
    }

    try
    {
        File.WriteAllText(currentFilePath, textView.Text!.ToString());
        isNoteDirty = false; // Reset dirty flag after manual save
        MessageBox.Query("Saved", $"Saved to {Path.GetFileName(currentFilePath)}", "OK");
    }
    catch (Exception ex)
    {
        MessageBox.ErrorQuery("Error", $"Could not save:\n{ex.Message}", "OK");
    }
}

void DeleteSelectedNote()
{
    if (string.IsNullOrEmpty(currentFilePath))
    {
        MessageBox.ErrorQuery("Error", "No note selected.", "OK");
        return;
    }

    var res = MessageBox.Query("Delete?", $"Delete {Path.GetFileName(currentFilePath)}?", "Yes", "No");
    if (res == 0)
    {
        try
        {
            File.Delete(currentFilePath);
            RefreshNoteList();
            textView.Text = "";
            currentFilePath = null;
            isNoteDirty = false;
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Error", ex.Message, "OK");
        }
    }
}

Application.Run();
autoSaveTimer?.Dispose();
Application.Shutdown();

public class NoteDSettings
{
    [JsonPropertyName("auto_save_enabled")]
    public bool AutoSaveEnabled { get; } = true;

    [JsonPropertyName("auto_save_delay_ms")]
    public int AutoSaveDelayMs { get; } = 5000;
    
    [JsonPropertyName("show_markdown_preview")]
    public bool ShowMarkdownPreview { get; }
}

public static class SettingsManager
{
    public static NoteDSettings Settings { get; private set; } = new ();

    public static string SettingsFilePath
    {
        get
        {
            var appDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NoteD"
            );
            Directory.CreateDirectory(appDataFolder);
            return Path.Combine(appDataFolder, "settings.json");
        }
    }

    public static void LoadSettings()
    {
        var path = SettingsFilePath;
        if (File.Exists(path))
        {
            try
            {
                var jsonString = File.ReadAllText(path);
                var loadedSettings = JsonSerializer.Deserialize<NoteDSettings>(jsonString);
                
                if (loadedSettings != null)
                {
                    Settings = loadedSettings;
                }
                else
                {
                    Settings = new NoteDSettings(); 
                    SaveSettings();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading settings: {ex.Message}");
                Settings = new NoteDSettings();
                SaveSettings();
            }
        }
        else
        {
            Settings = new NoteDSettings();
            SaveSettings();
        }
    }

    private static void SaveSettings()
    {
        var path = SettingsFilePath;
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var jsonString = JsonSerializer.Serialize(Settings, options);
            File.WriteAllText(path, jsonString);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving settings: {ex.Message}");
        }
    }
}