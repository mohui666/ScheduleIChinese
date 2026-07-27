# -*- coding: utf-8 -*-
"""List 2-3 word TitleCase entries with CJK values that might be generated mix names."""
import re
import sys

UI_WORDS = {
    'Management', 'Settings', 'Menu', 'Button', 'Panel', 'Screen', 'Mode', 'Shop',
    'Store', 'Mart', 'Hardware', 'Supplies', 'Inventory', 'Station', 'Table', 'Rack',
    'Light', 'Soil', 'Closet', 'Sprinkler', 'Press', 'Cauldron', 'Trimmers', 'Bean',
    'Chili', 'Iodine', 'Battery', 'Addy', 'Pseudo', 'Horse', 'Drying', 'Electric',
    'Spectrum', 'Large', 'Medium', 'Mega', 'Small', 'Storage', 'Grow', 'LED', 'Metal',
    'Glass', 'Wooden', 'Huge', 'Fertilizer', 'Energy', 'Drink', 'Air', 'Full', 'Big',
    'Quality', 'Price', 'Order', 'Cart', 'Game', 'Save', 'Load', 'New', 'Continue',
    'Options', 'Exit', 'Play', 'Day', 'Time', 'Daily', 'Weekly', 'Total', 'Item',
    'Delivery', 'Loading', 'Dock', 'Fee', 'Offense', 'Notice', 'Police', 'Chief',
}

for path in sys.argv[1:]:
    for line in open(path, encoding='utf-8'):
        line = line.rstrip('\n')
        if not line or line.startswith('#') or '=' not in line:
            continue
        k = line.split('=', 1)[0]
        if not re.match(r'^[A-Z][A-Za-z]+(?: [A-Z][A-Za-z]+){1,2}$', k):
            continue
        words = set(k.split())
        if words & UI_WORDS:
            continue
        print(line)
