import re
import glob
import os

theme_xaml = open('WinSetupHub.App/Theme/Theme.xaml', encoding='utf-8').read()
app_xaml = open('WinSetupHub.App/App.xaml', encoding='utf-8').read()

keys_in_theme = set(re.findall(r'x:Key="([^"]+)"', theme_xaml))
keys_in_theme.update(re.findall(r'x:Key="([^"]+)"', app_xaml))

xaml_files = glob.glob('WinSetupHub.App/**/*.xaml', recursive=True)

for xaml_path in xaml_files:
    if 'Theme.xaml' in xaml_path or 'App.xaml' in xaml_path:
        continue
    content = open(xaml_path, encoding='utf-8').read()
    local_keys = set(re.findall(r'x:Key="([^"]+)"', content))
    all_keys = keys_in_theme.union(local_keys)
    
    requested = set(re.findall(r'StaticResource\s+([^}\s]+)', content))
    missing = [r for r in requested if r not in all_keys and not r.startswith('{') and not r.endswith('}')]
    if missing:
        print(f"File: {xaml_path}")
        print(f"  Missing resources: {missing}")
