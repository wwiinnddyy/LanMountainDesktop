import xml.etree.ElementTree as ET
import re
import os

axaml_path = "LanMountainDesktop/Views/MainWindow.axaml"
with open(axaml_path, 'r', encoding='utf-8') as f:
    axaml_content = f.read()

# We'll use regex to find `<Grid x:Name="...SettingsPanel" ...>` or `<StackPanel x:Name="...SettingsPanel" ...>` ignoring WallpaperSettingsPanel
pattern = r'(?:<Grid|<StackPanel)\s+x:Name="([A-Za-z0-9]+SettingsPanel)"[\s\S]*?(?:</Grid>|</StackPanel>)\s*'

# Actually, parsing XAML with regex might be risky because of nested grids.
# Let's count matching open/close tags.

def extract_panel(content, panel_name):
    # Find start tag
    start_tag_regex = re.compile(r'<(Grid|StackPanel)[^>]*?x:Name="' + panel_name + r'"[^>]*?>')
    match = start_tag_regex.search(content)
    if not match:
        return None, None
    
    start_idx = match.start()
    tag_name = match.group(1)
    
    open_tag = f"<{tag_name}"
    close_tag = f"</{tag_name}>"
    
    # Track nesting
    nesting = 0
    i = start_idx
    while i < len(content):
        if content.startswith(open_tag, i):
            # check if it's not a self-closing tag!
            # A simple heuristic: find the end of the tag
            end_bracket = content.find('>', i)
            if end_bracket != -1 and content[end_bracket-1] == '/':
                # self closing, do nothing
                pass
            else:
                nesting += 1
            i += len(open_tag)
        elif content.startswith(close_tag, i):
            nesting -= 1
            if nesting == 0:
                end_idx = i + len(close_tag)
                return content[start_idx:end_idx], (start_idx, end_idx)
            i += len(close_tag)
        else:
            i += 1
            
    return None, None

panels_to_extract = [
    "GridSettingsPanel",
    "ColorSettingsPanel",
    "StatusBarSettingsPanel",
    "WeatherSettingsPanel",
    "RegionSettingsPanel",
    "UpdateSettingsPanel",
    "AboutSettingsPanel",
    "LauncherSettingsPanel",
    "PluginsSettingsPanel" # Might be PluginSettingsPanel, we'll check
]

# Quick check on actual names:
all_panels = re.findall(r'x:Name="([A-Za-z0-9]+SettingsPanel)"', axaml_content)
print("Found panels:", all_panels)
