using System;
using System.Collections.Generic;

namespace ScheduleIChinese
{
    /// <summary>
    /// Display-only translations for storefronts and large world signs.
    /// These bypass person-name restoration (Dan, Bleuball, Oscar, etc.)
    /// without changing any game-side business or location identifiers.
    /// </summary>
    internal static class StorefrontTranslations
    {
        private static readonly Dictionary<string, string> Names =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Barbershop", "理发店" },
                { "Bleuball's Boutique", "布鲁博尔精品店" },
                { "Bleuball Boutique", "布鲁博尔精品店" },
                { "Bleuball\nBoutique", "布鲁博尔\n精品店" },
                { "Body Shop", "车身修理店" },
                { "Body\nShop", "车身\n修理店" },
                { "Bud's Bar", "巴德酒吧" },
                { "Bud's\nBar", "巴德\n酒吧" },
                { "CAFE & BAKERY", "咖啡馆与烘焙坊" },
                { "Cafe & Bakery", "咖啡馆与烘焙坊" },
                { "Car Wash", "洗车店" },
                { "Casino", "赌场" },
                { "Chinese Restaurant", "中餐馆" },
                { "Chinese \nRestaurant", "中餐\n馆" },
                { "DAN'S  HARDWARE", "丹氏五金店" },
                { "Dan's  Hardware", "丹氏五金店" },
                { "Dan's Hardware", "丹氏五金店" },
                { "Dan's\nHardware", "丹氏\n五金店" },
                { "Docks Warehouse", "码头仓库" },
                { "Docks \nWarehouse", "码头区\n仓库" },
                { "Gas Station", "加油站" },
                { "Gas-Mart", "加油站超市" },
                { "Gas-Mart (Central)", "加油站超市（中心店）" },
                { "Gas-Mart (West)", "加油站超市（西区店）" },
                { "Handy Hank's Hardware", "汉克五金店" },
                { "Handy Hank's\nHardware", "汉克\n五金店" },
                { "Hardware Store", "五金店" },
                { "Hyland Bank", "海兰银行" },
                { "HYLAND\nBANK", "海兰\n银行" },
                { "Laundromat", "自助洗衣店" },
                { "Liquor Store", "酒水店" },
                { "Liquor \nStore", "酒水\n店" },
                { "Mega Beans", "超级豆咖啡店" },
                { "Motel", "汽车旅馆" },
                { "Motel Office", "汽车旅馆办公室" },
                { "Motel\nOffice", "汽车旅馆\n办公室" },
                { "Oscar's Store", "奥斯卡商店" },
                { "Pawn Shop", "当铺" },
                { "Pawn shop", "当铺" },
                { "Pharmacy", "药店" },
                { "PHARMACY", "药店" },
                { "Post Office", "邮局" },
                { "Slop Shop", "杂货店" },
                { "Storage Unit", "仓储中心" },
                { "Supermarket", "超市" },
                { "Sweatshop", "血汗工厂" },
                { "Taco Ticklers", "玉米饼乐园" },
                { "Taco\nTicklers", "玉米饼\n乐园" },
                { "Tattoo Shop", "纹身店" },
                { "THE BUTTER BOX", "奶油盒" },
                { "The Butter Box", "奶油盒" },
                { "The Butter\nBox", "奶油\n盒" },
                { "Thrifty Threads", "节俭衣坊" },
                { "Thrifty \nThreads", "节俭\n衣坊" },
                { "Thrifty\nThreads", "节俭\n衣坊" },

                // Property/business signs and other prominent map signage.
                { "Barn", "谷仓" },
                { "Businesses for sale", "待售企业" },
                { "Properties for sale", "待售房产" },
                { "Hyland Manor", "海兰庄园" },
                { "TOWN   SQUARE", "小镇广场" },
                { "Warehouse", "仓库" },
                { "Fire Station", "消防站" },
                { "Hyland Police Station", "海兰警察局" },

                // Fuel-grade lettering displayed on the gas-station signs.
                { "Regular Gas", "普通汽油" },
                { "Mega Gas", "超级汽油" },
                { "Sexy Gas", "劲爆汽油" },
                { "Garbage Gas", "劣质汽油" }
            };

        public static bool TryGet(string source, out string translated)
        {
            return Names.TryGetValue(source, out translated);
        }

        public static int Count => Names.Count;
    }
}
