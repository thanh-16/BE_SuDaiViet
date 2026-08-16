using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sử_Đại_Việt.Dtos;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Data
{
    public static class DatabaseInitializer
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider, ILogger? logger = null)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                logger?.LogInformation("🔄 Đang kiểm tra và khởi tạo cơ sở dữ liệu PostgreSQL cho 'Sử Đại Việt'...");
                var result = await RunFullInitializationAsync(context, logger);
                if (result.Success)
                {
                    logger?.LogInformation("✅ Khởi tạo và đồng bộ Database thành công: {StepCount} bước hoàn tất.", result.ExecutedSteps.Count);
                }
                else
                {
                    logger?.LogWarning("⚠️ Khởi tạo Database có cảnh báo: {Errors}", string.Join("; ", result.Errors));
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "❌ Lỗi nghiêm trọng trong quá trình khởi tạo Database.");
            }
        }

        public static async Task<DatabaseInitResult> RunFullInitializationAsync(ApplicationDbContext context, ILogger? logger = null)
        {
            var result = new DatabaseInitResult();

            // 1. Kiểm tra kết nối
            try
            {
                var conn = context.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                result.ExecutedSteps.Add("1. Kết nối cơ sở dữ liệu thành công.");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Không thể kết nối đến PostgreSQL: {ex.Message}";
                result.Errors.Add(ex.ToString());
                return result;
            }

            // 2. DDL Scripts chia theo từng khối độc lập
            var ddlBlocks = new List<(string Name, string Sql)>
            {
                ("Extensions & Schemas", @"
                    CREATE EXTENSION IF NOT EXISTS pgcrypto;
                    CREATE SCHEMA IF NOT EXISTS public;
                "),

                ("Common Trigger Functions", @"
                    CREATE OR REPLACE FUNCTION public.update_updated_at_column()
                    RETURNS TRIGGER AS $$
                    BEGIN
                        NEW.updated_at = timezone('utc'::text, now());
                        RETURN NEW;
                    END;
                    $$ LANGUAGE plpgsql;
                "),

                ("Profiles Table", @"
                    CREATE TABLE IF NOT EXISTS public.profiles (
                        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                        email VARCHAR(255) NULL,
                        phone VARCHAR(50) NULL,
                        display_name VARCHAR(50) DEFAULT 'Nghĩa Sĩ' NOT NULL,
                        avatar_url VARCHAR(500) NULL,
                        is_banned BOOLEAN DEFAULT false NOT NULL,
                        role VARCHAR(20) DEFAULT 'player' NOT NULL,
                        level INTEGER DEFAULT 1 NOT NULL,
                        experience INTEGER DEFAULT 0 NOT NULL,
                        created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        CONSTRAINT chk_profiles_role CHECK (role IN ('player', 'admin'))
                    );

                    DROP TRIGGER IF EXISTS trigger_update_profiles_updated_at ON public.profiles;
                    CREATE TRIGGER trigger_update_profiles_updated_at 
                        BEFORE UPDATE ON public.profiles 
                        FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
                "),

                ("Wallets Table & Trigger", @"
                    CREATE TABLE IF NOT EXISTS public.wallets (
                        user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE PRIMARY KEY,
                        gold_balance INTEGER DEFAULT 0 NOT NULL,
                        gem_balance INTEGER DEFAULT 0 NOT NULL,
                        version BIGINT DEFAULT 1 NOT NULL,
                        updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        CONSTRAINT chk_positive_gold CHECK (gold_balance >= 0),
                        CONSTRAINT chk_positive_gem CHECK (gem_balance >= 0)
                    );

                    CREATE OR REPLACE FUNCTION public.create_wallet_for_new_user()
                    RETURNS trigger AS $$
                    BEGIN
                        INSERT INTO public.wallets (user_id, gold_balance, gem_balance)
                        VALUES (NEW.id, 0, 0)
                        ON CONFLICT (user_id) DO NOTHING;
                        RETURN NEW;
                    END;
                    $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

                    DROP TRIGGER IF EXISTS on_profile_created_create_wallet ON public.profiles;
                    CREATE TRIGGER on_profile_created_create_wallet
                        AFTER INSERT ON public.profiles
                        FOR EACH ROW EXECUTE FUNCTION public.create_wallet_for_new_user();

                    DROP TRIGGER IF EXISTS trigger_update_wallets_updated_at ON public.wallets;
                    CREATE TRIGGER trigger_update_wallets_updated_at 
                        BEFORE UPDATE ON public.wallets 
                        FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
                "),

                ("Leaderboard Table", @"
                    CREATE TABLE IF NOT EXISTS public.leaderboard (
                        id BIGSERIAL PRIMARY KEY,
                        user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
                        username VARCHAR(50) NOT NULL,
                        score INTEGER DEFAULT 0 NOT NULL,
                        stage_reached VARCHAR(50) DEFAULT 'Ải 1' NOT NULL,
                        updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        CONSTRAINT leaderboard_user_id_unique UNIQUE(user_id)
                    );

                    CREATE INDEX IF NOT EXISTS leaderboard_score_idx ON public.leaderboard (score DESC);

                    DROP TRIGGER IF EXISTS trigger_update_leaderboard_updated_at ON public.leaderboard;
                    CREATE TRIGGER trigger_update_leaderboard_updated_at 
                        BEFORE UPDATE ON public.leaderboard 
                        FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

                    CREATE OR REPLACE FUNCTION public.sync_leaderboard_username()
                    RETURNS TRIGGER AS $$
                    BEGIN
                        IF OLD.display_name IS DISTINCT FROM NEW.display_name THEN
                            UPDATE public.leaderboard
                            SET username = NEW.display_name, updated_at = timezone('utc'::text, now())
                            WHERE user_id = NEW.id;
                        END IF;
                        RETURN NEW;
                    END;
                    $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

                    DROP TRIGGER IF EXISTS trigger_sync_leaderboard_username ON public.profiles;
                    CREATE TRIGGER trigger_sync_leaderboard_username
                        AFTER UPDATE ON public.profiles
                        FOR EACH ROW EXECUTE FUNCTION public.sync_leaderboard_username();
                "),

                ("Game Config Table", @"
                    CREATE TABLE IF NOT EXISTS public.game_config (
                        config_key VARCHAR(100) PRIMARY KEY,
                        config_value NUMERIC NOT NULL,
                        description TEXT,
                        updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
                    );
                "),

                ("Game Items Table", @"
                    CREATE TABLE IF NOT EXISTS public.game_items (
                        id VARCHAR(50) PRIMARY KEY,
                        name VARCHAR(100) NOT NULL,
                        description TEXT,
                        price_gold INTEGER DEFAULT 0 NOT NULL,
                        price_gem INTEGER DEFAULT 0 NOT NULL,
                        price_vnd INTEGER DEFAULT 0 NOT NULL,
                        item_type VARCHAR(30) DEFAULT 'Consumable' NOT NULL,
                        attributes JSONB DEFAULT NULL,
                        created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        CONSTRAINT chk_item_type CHECK (item_type IN ('Equipment', 'Consumable', 'Skin', 'Cosmetic'))
                    );

                    CREATE INDEX IF NOT EXISTS idx_items_attr_gin ON public.game_items USING GIN (attributes jsonb_path_ops);
                "),

                ("Player Inventories Table", @"
                    CREATE TABLE IF NOT EXISTS public.player_inventories (
                        id BIGSERIAL PRIMARY KEY,
                        user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
                        item_id VARCHAR(50) REFERENCES public.game_items(id) ON DELETE CASCADE NOT NULL,
                        quantity INTEGER DEFAULT 1 NOT NULL,
                        acquired_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        deleted_at TIMESTAMP WITH TIME ZONE DEFAULT NULL,
                        CONSTRAINT chk_positive_qty CHECK (quantity >= 0)
                    );

                    CREATE UNIQUE INDEX IF NOT EXISTS unique_user_item_active_idx ON public.player_inventories (user_id, item_id) WHERE deleted_at IS NULL;
                    CREATE INDEX IF NOT EXISTS idx_inventory_user ON public.player_inventories (user_id) WHERE deleted_at IS NULL;

                    DROP TRIGGER IF EXISTS trigger_update_player_inventories_updated_at ON public.player_inventories;
                    CREATE TRIGGER trigger_update_player_inventories_updated_at 
                        BEFORE UPDATE ON public.player_inventories 
                        FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

                    CREATE OR REPLACE FUNCTION public.auto_soft_delete_inventory()
                    RETURNS TRIGGER AS $$
                    BEGIN
                        IF NEW.quantity = 0 THEN
                            NEW.deleted_at := timezone('utc'::text, now());
                        END IF;
                        RETURN NEW;
                    END;
                    $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

                    DROP TRIGGER IF EXISTS trigger_auto_soft_delete_inventory ON public.player_inventories;
                    CREATE TRIGGER trigger_auto_soft_delete_inventory
                        BEFORE UPDATE ON public.player_inventories 
                        FOR EACH ROW EXECUTE FUNCTION public.auto_soft_delete_inventory();
                "),

                ("Player Heroes & Equipments Table", @"
                    CREATE TABLE IF NOT EXISTS public.player_heroes (
                        id BIGSERIAL PRIMARY KEY,
                        user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
                        hero_key VARCHAR(50) NOT NULL,
                        level INTEGER DEFAULT 1 NOT NULL,
                        experience INTEGER DEFAULT 0 NOT NULL,
                        skill_active_level INTEGER DEFAULT 1 NOT NULL,
                        skill_passive_level INTEGER DEFAULT 1 NOT NULL,
                        unlocked_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        CONSTRAINT unique_user_hero UNIQUE (user_id, hero_key),
                        CONSTRAINT chk_hero_key CHECK (hero_key IN ('hue', 'nhac', 'lu'))
                    );

                    DROP TRIGGER IF EXISTS trigger_update_player_heroes_updated_at ON public.player_heroes;
                    CREATE TRIGGER trigger_update_player_heroes_updated_at 
                        BEFORE UPDATE ON public.player_heroes 
                        FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

                    CREATE TABLE IF NOT EXISTS public.hero_equipments (
                        id BIGSERIAL PRIMARY KEY,
                        player_hero_id BIGINT REFERENCES public.player_heroes(id) ON DELETE CASCADE NOT NULL,
                        inventory_item_id BIGINT REFERENCES public.player_inventories(id) ON DELETE CASCADE NOT NULL,
                        slot_type VARCHAR(20) NOT NULL,
                        equipped_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        deleted_at TIMESTAMP WITH TIME ZONE DEFAULT NULL,
                        CONSTRAINT chk_slot_type CHECK (slot_type IN ('Weapon', 'Armor', 'Accessory'))
                    );

                    CREATE UNIQUE INDEX IF NOT EXISTS idx_hero_equipments_inventory_item_unique ON public.hero_equipments (inventory_item_id) WHERE deleted_at IS NULL;
                    CREATE UNIQUE INDEX IF NOT EXISTS unique_hero_slot_active_idx ON public.hero_equipments (player_hero_id, slot_type) WHERE deleted_at IS NULL;
                "),

                ("Partition Function & Master Partition Tables", @"
                    CREATE OR REPLACE FUNCTION public.ensure_monthly_partition(p_table TEXT, p_date TIMESTAMP WITH TIME ZONE)
                    RETURNS VOID AS $$
                    DECLARE
                        v_start_date TIMESTAMP WITH TIME ZONE := date_trunc('month', p_date);
                        v_end_date TIMESTAMP WITH TIME ZONE := v_start_date + INTERVAL '1 month';
                        v_partition_name TEXT := p_table || '_y' || to_char(v_start_date, 'YYYY') || 'm' || to_char(v_start_date, 'MM');
                    BEGIN
                        IF EXISTS (
                            SELECT 1 
                            FROM pg_catalog.pg_class c
                            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                            WHERE c.relname = LOWER(v_partition_name) 
                              AND n.nspname = 'public'
                        ) THEN
                            RETURN;
                        END IF;

                        EXECUTE format(
                            'CREATE TABLE IF NOT EXISTS %I PARTITION OF %I FOR VALUES FROM (%L) TO (%L)',
                            v_partition_name, p_table, v_start_date, v_end_date
                        );
                    END;
                    $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

                    CREATE TABLE IF NOT EXISTS public.mailbox (
                        id BIGSERIAL,
                        receiver_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE,
                        title VARCHAR(150) NOT NULL,
                        content TEXT NOT NULL,
                        attachments JSONB DEFAULT NULL,
                        is_read BOOLEAN DEFAULT false NOT NULL,
                        is_claimed BOOLEAN DEFAULT false NOT NULL,
                        expired_at TIMESTAMP WITH TIME ZONE,
                        created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        deleted_at TIMESTAMP WITH TIME ZONE DEFAULT NULL,
                        PRIMARY KEY (id, created_at)
                    ) PARTITION BY RANGE (created_at);

                    CREATE TABLE IF NOT EXISTS public.player_broadcast_claims (
                        id BIGSERIAL PRIMARY KEY,
                        user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
                        mail_id BIGINT NOT NULL,
                        is_read BOOLEAN DEFAULT true NOT NULL,
                        is_claimed BOOLEAN DEFAULT false NOT NULL,
                        claimed_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        CONSTRAINT unique_user_broadcast_claim UNIQUE (user_id, mail_id)
                    );

                    CREATE TABLE IF NOT EXISTS public.battle_logs (
                        id BIGSERIAL,
                        user_id UUID NOT NULL,
                        stage_id VARCHAR(50) NOT NULL,
                        battle_status VARCHAR(20) NOT NULL,
                        duration_seconds INTEGER NOT NULL,
                        score_earned INTEGER DEFAULT 0 NOT NULL,
                        gold_earned INTEGER DEFAULT 0 NOT NULL,
                        enemies_killed INTEGER DEFAULT 0 NOT NULL,
                        battle_metadata JSONB DEFAULT NULL,
                        created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        CONSTRAINT chk_battle_status CHECK (battle_status IN ('Victory', 'Defeat', 'Aborted')),
                        PRIMARY KEY (id, created_at)
                    ) PARTITION BY RANGE (created_at);

                    CREATE TABLE IF NOT EXISTS public.admin_logs (
                        id BIGSERIAL PRIMARY KEY,
                        admin_username VARCHAR(100) NOT NULL,
                        action_name VARCHAR(100) NOT NULL,
                        action_details TEXT NOT NULL,
                        created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS public.player_audit_logs (
                        id BIGSERIAL,
                        user_id UUID NOT NULL,
                        action_type VARCHAR(50) NOT NULL,
                        details TEXT NOT NULL,
                        ip_address VARCHAR(45) NULL,
                        created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        PRIMARY KEY (id, created_at)
                    ) PARTITION BY RANGE (created_at);

                    CREATE TABLE IF NOT EXISTS public.transactions (
                        id BIGSERIAL,
                        user_id UUID NOT NULL,
                        transaction_type VARCHAR(30) NOT NULL,
                        amount_vnd INTEGER DEFAULT 0 NOT NULL,
                        amount_gold INTEGER DEFAULT 0 NOT NULL,
                        amount_gem INTEGER DEFAULT 0 NOT NULL,
                        payment_method VARCHAR(50) NULL,
                        reference_id VARCHAR(100) NULL,
                        status VARCHAR(20) DEFAULT 'Pending' NOT NULL,
                        created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        CONSTRAINT chk_partition_tx_status CHECK (status IN ('Pending', 'Completed', 'Failed', 'Refunded')),
                        PRIMARY KEY (id, created_at)
                    ) PARTITION BY RANGE (created_at);

                    CREATE OR REPLACE FUNCTION public.route_and_create_partition_trigger()
                    RETURNS TRIGGER AS $$
                    BEGIN
                        IF TG_TABLE_NAME::text NOT IN ('mailbox', 'battle_logs', 'player_audit_logs', 'transactions') THEN
                            RETURN NEW;
                        END IF;
                        PERFORM public.ensure_monthly_partition(TG_TABLE_NAME::text, NEW.created_at);
                        RETURN NEW;
                    END;
                    $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

                    DROP TRIGGER IF EXISTS trigger_mailbox_partition_on_demand ON public.mailbox;
                    CREATE TRIGGER trigger_mailbox_partition_on_demand
                        BEFORE INSERT ON public.mailbox
                        FOR EACH ROW EXECUTE FUNCTION public.route_and_create_partition_trigger();

                    DROP TRIGGER IF EXISTS trigger_battle_logs_partition_on_demand ON public.battle_logs;
                    CREATE TRIGGER trigger_battle_logs_partition_on_demand
                        BEFORE INSERT ON public.battle_logs
                        FOR EACH ROW EXECUTE FUNCTION public.route_and_create_partition_trigger();

                    DROP TRIGGER IF EXISTS trigger_player_audit_logs_partition_on_demand ON public.player_audit_logs;
                    CREATE TRIGGER trigger_player_audit_logs_partition_on_demand
                        BEFORE INSERT ON public.player_audit_logs
                        FOR EACH ROW EXECUTE FUNCTION public.route_and_create_partition_trigger();

                    DROP TRIGGER IF EXISTS trigger_transactions_partition_on_demand ON public.transactions;
                    CREATE TRIGGER trigger_transactions_partition_on_demand
                        BEFORE INSERT ON public.transactions
                        FOR EACH ROW EXECUTE FUNCTION public.route_and_create_partition_trigger();
                "),

                ("Marketplace Tables", @"
                    CREATE TABLE IF NOT EXISTS public.marketplace_listings (
                        id BIGSERIAL PRIMARY KEY,
                        seller_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
                        buyer_id UUID REFERENCES public.profiles(id) ON DELETE SET NULL NULL,
                        inventory_item_id BIGINT REFERENCES public.player_inventories(id) ON DELETE CASCADE NOT NULL,
                        price_gold INTEGER DEFAULT 0 NOT NULL,
                        price_gem INTEGER DEFAULT 0 NOT NULL,
                        listing_fee INTEGER DEFAULT 0 NOT NULL,
                        status VARCHAR(20) DEFAULT 'Active' NOT NULL,
                        created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        deleted_at TIMESTAMP WITH TIME ZONE DEFAULT NULL,
                        CONSTRAINT chk_listings_status CHECK (status IN ('Active', 'Reserved', 'Sold', 'Cancelled')),
                        CONSTRAINT chk_listings_price_gold CHECK (price_gold >= 0),
                        CONSTRAINT chk_listings_price_gem CHECK (price_gem >= 0)
                    );

                    DROP TRIGGER IF EXISTS trigger_update_marketplace_listings_updated_at ON public.marketplace_listings;
                    CREATE TRIGGER trigger_update_marketplace_listings_updated_at 
                        BEFORE UPDATE ON public.marketplace_listings 
                        FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

                    CREATE TABLE IF NOT EXISTS public.marketplace_reservations (
                        id BIGSERIAL PRIMARY KEY,
                        listing_id BIGINT REFERENCES public.marketplace_listings(id) ON DELETE CASCADE NOT NULL,
                        user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
                        reserved_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        CONSTRAINT unique_listing_reservation UNIQUE (listing_id)
                    );

                    CREATE INDEX IF NOT EXISTS idx_reservations_listing_user ON public.marketplace_reservations (listing_id, user_id);
                "),

                ("Quests & Achievements Tables", @"
                    CREATE TABLE IF NOT EXISTS public.game_quests (
                        id VARCHAR(100) PRIMARY KEY,
                        title VARCHAR(150) NOT NULL,
                        description TEXT,
                        target_count INTEGER DEFAULT 1 NOT NULL,
                        gold_reward INTEGER DEFAULT 0 NOT NULL,
                        gem_reward INTEGER DEFAULT 0 NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS public.game_achievements (
                        id VARCHAR(100) PRIMARY KEY,
                        title VARCHAR(150) NOT NULL,
                        description TEXT,
                        gold_reward INTEGER DEFAULT 0 NOT NULL,
                        gem_reward INTEGER DEFAULT 0 NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS public.game_sessions (
                        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                        user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
                        ip_address VARCHAR(45) NULL,
                        client_version VARCHAR(20) NOT NULL,
                        started_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        ended_at TIMESTAMP WITH TIME ZONE DEFAULT NULL,
                        duration_seconds INTEGER DEFAULT NULL
                    );

                    CREATE TABLE IF NOT EXISTS public.quest_progress (
                        id BIGSERIAL PRIMARY KEY,
                        user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
                        quest_id VARCHAR(100) REFERENCES public.game_quests(id) ON DELETE CASCADE NOT NULL,
                        status VARCHAR(20) DEFAULT 'Accepted' NOT NULL,
                        progress_count INTEGER DEFAULT 0 NOT NULL,
                        target_count INTEGER DEFAULT 1 NOT NULL,
                        updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        CONSTRAINT unique_user_quest UNIQUE (user_id, quest_id),
                        CONSTRAINT chk_quest_status CHECK (status IN ('Accepted', 'Completed', 'Rewarded'))
                    );

                    CREATE TABLE IF NOT EXISTS public.achievements (
                        user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
                        achievement_id VARCHAR(100) REFERENCES public.game_achievements(id) ON DELETE CASCADE NOT NULL,
                        unlocked_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
                        PRIMARY KEY (user_id, achievement_id)
                    );
                "),

                ("Stored Procedures", @"
                    CREATE OR REPLACE FUNCTION public.buy_shop_item(
                        p_user_id UUID,
                        p_item_id VARCHAR,
                        p_currency VARCHAR
                    )
                    RETURNS VARCHAR AS $$
                    DECLARE
                        v_gold_price INT;
                        v_gem_price INT;
                        v_gold_balance INT;
                        v_gem_balance INT;
                        v_is_banned BOOLEAN;
                    BEGIN
                        SELECT gold_balance, gem_balance INTO v_gold_balance, v_gem_balance
                        FROM public.wallets
                        WHERE user_id = p_user_id
                        FOR UPDATE;

                        IF NOT FOUND THEN
                            RETURN 'ERROR: Không tìm thấy ví của người chơi.';
                        END IF;

                        SELECT is_banned INTO v_is_banned FROM public.profiles WHERE id = p_user_id;
                        IF v_is_banned THEN
                            RETURN 'ERROR: Tài khoản của nghĩa sĩ đã bị khóa.';
                        END IF;

                        SELECT price_gold, price_gem INTO v_gold_price, v_gem_price
                        FROM public.game_items
                        WHERE id = p_item_id;

                        IF NOT FOUND THEN
                            RETURN 'ERROR: Không tìm thấy vật phẩm.';
                        END IF;

                        IF LOWER(p_currency) = 'gold' THEN
                            IF v_gold_price <= 0 THEN
                                RETURN 'ERROR: Vật phẩm không bán bằng Vàng.';
                            END IF;
                            IF v_gold_balance < v_gold_price THEN
                                RETURN 'ERROR: Số dư Vàng không đủ.';
                            END IF;
                            UPDATE public.wallets 
                            SET gold_balance = gold_balance - v_gold_price, version = version + 1, updated_at = now()
                            WHERE user_id = p_user_id;
                        ELSIF LOWER(p_currency) = 'gem' THEN
                            IF v_gem_price <= 0 THEN
                                RETURN 'ERROR: Vật phẩm không bán bằng Ngọc.';
                            END IF;
                            IF v_gem_balance < v_gem_price THEN
                                RETURN 'ERROR: Số dư Ngọc không đủ.';
                            END IF;
                            UPDATE public.wallets 
                            SET gem_balance = gem_balance - v_gem_price, version = version + 1, updated_at = now()
                            WHERE user_id = p_user_id;
                        ELSE
                            RETURN 'ERROR: Đơn vị thanh toán không hợp lệ.';
                        END IF;

                        INSERT INTO public.player_inventories (user_id, item_id, quantity, acquired_at, updated_at)
                        VALUES (p_user_id, p_item_id, 1, now(), now())
                        ON CONFLICT (user_id, item_id) WHERE deleted_at IS NULL DO UPDATE
                        SET quantity = public.player_inventories.quantity + 1, acquired_at = now(), updated_at = now(), deleted_at = NULL;

                        INSERT INTO public.transactions (user_id, transaction_type, amount_gold, amount_gem, status, created_at, updated_at)
                        VALUES (
                            p_user_id, 
                            'ShopPurchase', 
                            CASE WHEN LOWER(p_currency) = 'gold' THEN -v_gold_price ELSE 0 END,
                            CASE WHEN LOWER(p_currency) = 'gem' THEN -v_gem_price ELSE 0 END,
                            'Completed',
                            now(),
                            now()
                        );

                        RETURN 'SUCCESS';
                    END;
                    $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

                    CREATE OR REPLACE FUNCTION public.cleanup_expired_reservations()
                    RETURNS VOID AS $$
                    DECLARE
                        v_expired_listing_id BIGINT;
                    BEGIN
                        FOR v_expired_listing_id IN
                            SELECT listing_id FROM public.marketplace_reservations
                            WHERE (now() - reserved_at) >= INTERVAL '5 minutes'
                        LOOP
                            DELETE FROM public.marketplace_reservations WHERE listing_id = v_expired_listing_id;
                            UPDATE public.marketplace_listings
                            SET status = 'Active', updated_at = now()
                            WHERE id = v_expired_listing_id AND status = 'Reserved';
                        END LOOP;
                    END;
                    $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

                    CREATE OR REPLACE FUNCTION public.buy_marketplace_item(
                        p_buyer_id UUID,
                        p_listing_id BIGINT
                    )
                    RETURNS VARCHAR AS $$
                    DECLARE
                        v_seller_id UUID;
                        v_item_id VARCHAR;
                        v_price_gold INT;
                        v_price_gem INT;
                        v_inv_id BIGINT;
                        v_status VARCHAR;
                        v_gold_balance INT;
                        v_gem_balance INT;
                        v_gold_tax INT;
                        v_gem_tax INT;
                        v_gold_payout INT;
                        v_gem_payout INT;
                        v_first_id UUID;
                        v_second_id UUID;
                        v_reserved_user UUID;
                        v_reserved_at TIMESTAMP WITH TIME ZONE;
                    BEGIN
                        PERFORM public.cleanup_expired_reservations();

                        SELECT seller_id, inventory_item_id, price_gold, price_gem, status
                        INTO v_seller_id, v_inv_id, v_price_gold, v_price_gem, v_status
                        FROM public.marketplace_listings
                        WHERE id = p_listing_id
                        FOR UPDATE;

                        IF NOT FOUND THEN
                            RETURN 'ERROR: Bài đăng bán không tồn tại.';
                        END IF;

                        IF v_status != 'Active' AND v_status != 'Reserved' THEN
                            RETURN 'ERROR: Bài đăng đã hoàn thành hoặc đã bị hủy.';
                        END IF;

                        IF v_seller_id = p_buyer_id THEN
                            RETURN 'ERROR: Không thể tự mua hàng của chính mình.';
                        END IF;

                        SELECT user_id, reserved_at INTO v_reserved_user, v_reserved_at
                        FROM public.marketplace_reservations
                        WHERE listing_id = p_listing_id;

                        IF FOUND THEN
                            IF v_reserved_user != p_buyer_id AND (now() - v_reserved_at) < INTERVAL '5 minutes' THEN
                                RETURN 'ERROR: Vật phẩm này đã được đặt chỗ giữ hàng bởi nghĩa sĩ khác.';
                            END IF;
                            DELETE FROM public.marketplace_reservations WHERE listing_id = p_listing_id;
                        END IF;

                        IF p_buyer_id < v_seller_id THEN
                            v_first_id := p_buyer_id;
                            v_second_id := v_seller_id;
                        ELSE
                            v_first_id := v_seller_id;
                            v_second_id := p_buyer_id;
                        END IF;

                        PERFORM 1 FROM public.wallets WHERE user_id = v_first_id FOR UPDATE;
                        PERFORM 1 FROM public.wallets WHERE user_id = v_second_id FOR UPDATE;

                        SELECT gold_balance, gem_balance INTO v_gold_balance, v_gem_balance
                        FROM public.wallets WHERE user_id = p_buyer_id;

                        IF v_price_gold > 0 AND v_gold_balance < v_price_gold THEN
                            RETURN 'ERROR: Số dư Vàng người mua không đủ.';
                        END IF;
                        IF v_price_gem > 0 AND v_gem_balance < v_price_gem THEN
                            RETURN 'ERROR: Số dư Ngọc người mua không đủ.';
                        END IF;

                        UPDATE public.wallets
                        SET gold_balance = gold_balance - v_price_gold,
                            gem_balance = gem_balance - v_price_gem,
                            version = version + 1,
                            updated_at = now()
                        WHERE user_id = p_buyer_id;

                        v_gold_tax := CEIL(v_price_gold * 0.05);
                        v_gem_tax := CEIL(v_price_gem * 0.05);
                        v_gold_payout := v_price_gold - v_gold_tax;
                        v_gem_payout := v_price_gem - v_gem_tax;

                        UPDATE public.wallets
                        SET gold_balance = gold_balance + v_gold_payout,
                            gem_balance = gem_balance + v_gem_payout,
                            version = version + 1,
                            updated_at = now()
                        WHERE user_id = v_seller_id;

                        SELECT item_id INTO v_item_id FROM public.player_inventories WHERE id = v_inv_id;

                        INSERT INTO public.player_inventories (user_id, item_id, quantity, acquired_at, updated_at)
                        VALUES (p_buyer_id, v_item_id, 1, now(), now())
                        ON CONFLICT (user_id, item_id) WHERE deleted_at IS NULL DO UPDATE
                        SET quantity = public.player_inventories.quantity + 1, acquired_at = now(), updated_at = now(), deleted_at = NULL;

                        UPDATE public.marketplace_listings
                        SET status = 'Sold', buyer_id = p_buyer_id, updated_at = now()
                        WHERE id = p_listing_id;

                        INSERT INTO public.transactions (user_id, transaction_type, amount_gold, amount_gem, reference_id, status, created_at, updated_at)
                        VALUES (p_buyer_id, 'MarketplacePurchase', -v_price_gold, -v_price_gem, 'LISTING-' || p_listing_id, 'Completed', now(), now());

                        INSERT INTO public.transactions (user_id, transaction_type, amount_gold, amount_gem, reference_id, status, created_at, updated_at)
                        VALUES (v_seller_id, 'MarketplaceSale', v_gold_payout, v_gem_payout, 'LISTING-' || p_listing_id, 'Completed', now(), now());

                        RETURN 'SUCCESS';
                    END;
                    $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

                    CREATE OR REPLACE FUNCTION public.claim_mailbox_attachments(
                        p_user_id UUID,
                        p_mail_id BIGINT
                    )
                    RETURNS VARCHAR AS $$
                    DECLARE
                        v_receiver_id UUID;
                        v_attachments JSONB;
                        v_is_claimed BOOLEAN;
                        v_expired_at TIMESTAMP WITH TIME ZONE;
                        v_gold_reward INT := 0;
                        v_gem_reward INT := 0;
                        v_item_record RECORD;
                        v_affected INT;
                    BEGIN
                        SELECT receiver_id, attachments, is_claimed, expired_at
                        INTO v_receiver_id, v_attachments, v_is_claimed, v_expired_at
                        FROM public.mailbox
                        WHERE id = p_mail_id
                        FOR UPDATE;

                        IF NOT FOUND THEN
                            RETURN 'ERROR: Thư không tồn tại.';
                        END IF;

                        IF v_expired_at IS NOT NULL AND v_expired_at < now() THEN
                            RETURN 'ERROR: Thư này đã hết hạn.';
                        END IF;

                        IF v_attachments IS NULL THEN
                            RETURN 'ERROR: Thư này không có quà đính kèm.';
                        END IF;

                        IF v_receiver_id IS NOT NULL THEN
                            IF v_receiver_id != p_user_id THEN
                                RETURN 'ERROR: Thư này không thuộc sở hữu của bạn.';
                            END IF;
                            IF v_is_claimed THEN
                                RETURN 'ERROR: Quà đã được nhận trước đó.';
                            END IF;
                            UPDATE public.mailbox SET is_claimed = true, is_read = true, updated_at = now() WHERE id = p_mail_id;
                        ELSE
                            INSERT INTO public.player_broadcast_claims (user_id, mail_id, is_read, is_claimed, claimed_at)
                            VALUES (p_user_id, p_mail_id, true, true, now())
                            ON CONFLICT (user_id, mail_id) DO NOTHING;
                            
                            GET DIAGNOSTICS v_affected = ROW_COUNT;
                            IF v_affected = 0 THEN
                                RETURN 'ERROR: Nghĩa sĩ đã nhận quà từ thư broadcast này rồi.';
                            END IF;
                        END IF;

                        PERFORM 1 FROM public.wallets WHERE user_id = p_user_id FOR UPDATE;

                        IF v_attachments ? 'gold' THEN
                            v_gold_reward := (v_attachments->>'gold')::INT;
                        END IF;
                        IF v_attachments ? 'gems' THEN
                            v_gem_reward := (v_attachments->>'gems')::INT;
                        END IF;

                        IF v_gold_reward > 0 OR v_gem_reward > 0 THEN
                            UPDATE public.wallets
                            SET gold_balance = gold_balance + v_gold_reward,
                                gem_balance = gem_balance + v_gem_reward,
                                version = version + 1,
                                updated_at = now()
                            WHERE user_id = p_user_id;

                            INSERT INTO public.transactions (user_id, transaction_type, amount_gold, amount_gem, reference_id, status, created_at, updated_at)
                            VALUES (p_user_id, 'MailboxClaim', v_gold_reward, v_gem_reward, 'MAIL-' || p_mail_id, 'Completed', now(), now());
                        END IF;

                        IF v_attachments ? 'items' AND jsonb_typeof(v_attachments->'items') = 'array' THEN
                            FOR v_item_record IN 
                                SELECT (value->>'id')::VARCHAR AS item_id, (value->>'qty')::INT AS qty
                                FROM jsonb_array_elements(v_attachments->'items')
                            LOOP
                                IF v_item_record.item_id IS NOT NULL AND v_item_record.qty > 0 THEN
                                    INSERT INTO public.player_inventories (user_id, item_id, quantity, acquired_at, updated_at)
                                    VALUES (p_user_id, v_item_record.item_id, v_item_record.qty, now(), now())
                                    ON CONFLICT (user_id, item_id) WHERE deleted_at IS NULL DO UPDATE
                                    SET quantity = public.player_inventories.quantity + v_item_record.qty, acquired_at = now(), updated_at = now(), deleted_at = NULL;
                                END IF;
                            END LOOP;
                        END IF;

                        RETURN 'SUCCESS';
                    END;
                    $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;
                ")
            };

            // Thực thi từng khối DDL
            foreach (var block in ddlBlocks)
            {
                try
                {
                    await context.Database.ExecuteSqlRawAsync(block.Sql);
                    result.ExecutedSteps.Add($"Thực thi khối DDL '{block.Name}' thành công.");
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Lỗi tại khối '{block.Name}': {ex.Message}");
                    logger?.LogWarning(ex, "Cảnh báo khi chạy DDL '{BlockName}'", block.Name);
                }
            }

            // 3. Pre-create các phân vùng từ 2026-01 đến 2027-12
            try
            {
                var partitionSql = @"
                    DO $$
                    DECLARE
                        v_date TIMESTAMP WITH TIME ZONE := '2026-01-01 00:00:00+00';
                        v_end TIMESTAMP WITH TIME ZONE := '2027-12-01 00:00:00+00';
                    BEGIN
                        WHILE v_date <= v_end LOOP
                            PERFORM public.ensure_monthly_partition('mailbox', v_date);
                            PERFORM public.ensure_monthly_partition('battle_logs', v_date);
                            PERFORM public.ensure_monthly_partition('player_audit_logs', v_date);
                            PERFORM public.ensure_monthly_partition('transactions', v_date);
                            v_date := v_date + INTERVAL '1 month';
                        END LOOP;
                    END;
                    $$;
                ";
                await context.Database.ExecuteSqlRawAsync(partitionSql);
                result.ExecutedSteps.Add("Tạo phân vùng hàng tháng (2026 - 2027) cho mailbox, battle_logs, player_audit_logs, transactions.");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Lỗi tạo phân vùng: {ex.Message}");
            }

            // 4. Seed dữ liệu mặc định bắt buộc: Game Items, Game Config, Quests, Achievements
            try
            {
                var seedBaseDataSql = @"
                    -- Game Configs
                    INSERT INTO public.game_config (config_key, config_value, description)
                    VALUES 
                        ('hue_damage', 25.0, 'Sát thương cơ bản của Nguyễn Huệ - Lối đánh uy lực, tầm trung'),
                        ('nhac_damage', 18.0, 'Sát thương cơ bản của Nguyễn Nhạc - Lối đánh nhanh nhẹn, tầm gần'),
                        ('lu_damage', 14.0, 'Sát thương cơ bản của Nguyễn Lữ - Lối đánh biến hóa, tầm xa'),
                        ('hue_speed', 320.0, 'Tốc độ di chuyển của Nguyễn Huệ'),
                        ('nhac_speed', 280.0, 'Tốc độ di chuyển của Nguyễn Nhạc'),
                        ('lu_speed', 380.0, 'Tốc độ di chuyển của Nguyễn Lữ')
                    ON CONFLICT (config_key) DO UPDATE 
                    SET config_value = EXCLUDED.config_value, description = EXCLUDED.description;

                    -- Game Items
                    INSERT INTO public.game_items (id, name, description, price_gold, price_gem, price_vnd, item_type, attributes)
                    VALUES
                        ('tran_thao_son_tra', 'Trân Thảo Sơn Trà', 'Hồi đầy 100% sinh lực cho nghĩa sĩ ngay tức khắc.', 80, 0, 0, 'Consumable', '{""slot"": ""none"", ""effect"": ""heal"", ""value"": 100.0, ""duration"": 0.0}'),
                        ('linh_dan_hoi_khi', 'Linh Đan Hồi Khí', 'Nạp đầy Nộ Khí để tung tuyệt kỹ liền tay.', 60, 0, 0, 'Consumable', '{""slot"": ""none"", ""effect"": ""rage"", ""value"": 100.0, ""duration"": 0.0}'),
                        ('ruou_de_quy_nhon', 'Rượu Đế Quy Nhơn', 'Tăng 30% sát thương trong 30 giây xung trận.', 200, 0, 0, 'Consumable', '{""slot"": ""none"", ""effect"": ""dmg_buff"", ""value"": 0.30, ""duration"": 30.0}'),
                        ('khien_dong_son', 'Khiên Đồng Đông Sơn', 'Lá chắn đồng bất hoại, miễn nhiễm sát thương 12 giây.', 250, 0, 0, 'Consumable', '{""slot"": ""none"", ""effect"": ""shield"", ""value"": 0.0, ""duration"": 12.0}'),
                        ('co_dao_phuc_sinh', 'Cờ Đào Phục Sinh', 'Hồi sinh tại trận một lần (50% máu) khi nghĩa sĩ gục ngã.', 500, 0, 0, 'Consumable', '{""slot"": ""none"", ""effect"": ""revive"", ""value"": 0.0, ""duration"": 0.0}'),
                        ('hoang_de_co_dao', 'Hoàng Đế Cổ Đao', 'Đại đao hoàng triều — vĩnh viễn +12% sát thương.', 640, 0, 0, 'Equipment', '{""slot"": ""weapon"", ""effect"": ""equip_dmg"", ""value"": 0.12, ""duration"": 0.0}'),
                        ('co_kiem_binh_dinh', 'Cổ Kiếm Bình Định', 'Bảo kiếm khai quốc — vĩnh viễn +18% sát thương.', 1200, 0, 0, 'Equipment', '{""slot"": ""weapon"", ""effect"": ""equip_dmg"", ""value"": 0.18, ""duration"": 0.0}'),
                        ('thiet_thuong_tayson', 'Thiết Thương Tây Sơn', 'Trường thương bọc sắt — vĩnh viễn +25% sát thương.', 2600, 0, 0, 'Equipment', '{""slot"": ""weapon"", ""effect"": ""equip_dmg"", ""value"": 0.25, ""duration"": 0.0}'),
                        ('song_thiet_con', 'Song Thiết Côn', 'Côn sắt song đầu — vĩnh viễn +32% sát thương.', 4200, 0, 0, 'Equipment', '{""slot"": ""weapon"", ""effect"": ""equip_dmg"", ""value"": 0.32, ""duration"": 0.0}'),
                        ('than_kinh_tayson', 'Tây Sơn Thần Kính', 'Thần khí tối thượng — vĩnh viễn +45% sát thương.', 0, 300, 0, 'Equipment', '{""slot"": ""weapon"", ""effect"": ""equip_dmg"", ""value"": 0.45, ""duration"": 0.0}'),
                        ('an_ngoc_hoang_de', 'Ấn Ngọc Hoàng Đế', 'Ấn ngọc danh giá — biểu tượng bậc đế vương (trang trí hồ sơ).', 0, 120, 0, 'Cosmetic', '{""slot"": ""none"", ""effect"": ""none"", ""value"": 0.0, ""duration"": 0.0}'),
                        ('giap_da_tayson', 'Tây Sơn Giáp Da', 'Áo giáp da dẻo dai — tăng 20% sinh lực tối đa.', 1000, 0, 0, 'Equipment', '{""slot"": ""armor"", ""effect"": ""equip_hp"", ""value"": 0.20, ""duration"": 0.0}'),
                        ('thiet_giap_tayson', 'Tây Sơn Thiết Giáp', 'Giáp sắt kiên cố của nghĩa quân — tăng 40% sinh lực tối đa.', 2500, 0, 0, 'Equipment', '{""slot"": ""armor"", ""effect"": ""equip_hp"", ""value"": 0.40, ""duration"": 0.0}'),
                        ('hoang_gia_chien_giap', 'Hoàng Gia Chiến Giáp', 'Chiến giáp hoàng triều đúc bằng đồng quý — tăng 70% sinh lực tối đa.', 5000, 0, 0, 'Equipment', '{""slot"": ""armor"", ""effect"": ""equip_hp"", ""value"": 0.70, ""duration"": 0.0}'),
                        ('bao_tinh_giap', 'Bảo Tinh Giáp', 'Thần giáp bảo thạch hộ thân — tăng 100% sinh lực tối đa.', 0, 400, 0, 'Equipment', '{""slot"": ""armor"", ""effect"": ""equip_hp"", ""value"": 1.00, ""duration"": 0.0}'),
                        ('equipment_weapon_long_tinh_dao', 'Long Tinh Đao', 'Đại đao khắc họa long hình tôn nghiêm — vĩnh viễn +35% sát thương.', 5000, 150, 0, 'Equipment', '{""slot"": ""weapon"", ""effect"": ""equip_dmg"", ""value"": 0.35, ""duration"": 0.0, ""icon_path"": ""res://assets/sprites/items/long_tinh_dao.png""}'),
                        ('equipment_armor_hac_ho_giap', 'Hắc Hổ Thiết Giáp', 'Thiết giáp khắc họa hình hổ đen dũng mãnh — tăng 55% sinh lực tối đa.', 3500, 100, 0, 'Equipment', '{""slot"": ""armor"", ""effect"": ""equip_hp"", ""value"": 0.55, ""duration"": 0.0, ""icon_path"": ""res://assets/sprites/items/hac_ho_giap.png""}'),
                        ('equipment_weapon_than_co_thuong', 'Thần Cơ Thương', 'Bảo khí súng hỏa mai Thần Cơ cải tiến — vĩnh viễn +40% sát thương.', 4500, 200, 0, 'Equipment', '{""slot"": ""weapon"", ""effect"": ""equip_dmg"", ""value"": 0.40, ""duration"": 0.0, ""icon_path"": ""res://assets/sprites/items/than_co_thuong.png""}')
                    ON CONFLICT (id) DO NOTHING;

                    -- Game Quests
                    INSERT INTO public.game_quests (id, title, description, target_count, gold_reward, gem_reward)
                    VALUES
                        ('quest_rach_gam_01', 'Chiến Thắng Rạch Gầm', 'Đánh bại quân Xiêm tại Rạch Gầm - Xoài Mút', 1, 1000, 50),
                        ('quest_ngoc_hoi_01', 'Đại Phá Ngọc Hồi', 'Đại phá đồn Ngọc Hồi quân Thanh', 1, 2000, 100)
                    ON CONFLICT (id) DO NOTHING;

                    -- Game Achievements
                    INSERT INTO public.game_achievements (id, title, description, gold_reward, gem_reward)
                    VALUES
                        ('ach_first_victory', 'Chiến Công Đầu', 'Giành chiến thắng trận đầu tiên', 100, 10),
                        ('ach_max_level', 'Vạn Nhân Địch', 'Đạt cấp độ tối đa', 5000, 500)
                    ON CONFLICT (id) DO NOTHING;
                ";
                await context.Database.ExecuteSqlRawAsync(seedBaseDataSql);
                result.ExecutedSteps.Add("Đã nạp / cập nhật dữ liệu mẫu cho Game Configs, Game Items, Quests, Achievements.");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Lỗi nạp Base Data: {ex.Message}");
            }

            // 5. Tự động đồng bộ tài khoản thật từ auth.users sang public.profiles nếu chưa có
            try
            {
                var syncRealUsersSql = @"
                    INSERT INTO public.profiles (id, email, phone, display_name, avatar_url, role, level, experience, created_at, updated_at)
                    SELECT 
                        u.id,
                        u.email,
                        u.phone,
                        COALESCE(
                            u.raw_user_meta_data->>'full_name',
                            u.raw_user_meta_data->>'display_name',
                            u.raw_user_meta_data->>'name',
                            SPLIT_PART(u.email, '@', 1),
                            'Nghĩa Sĩ'
                        ) as display_name,
                        COALESCE(
                            u.raw_user_meta_data->>'avatar_url',
                            u.raw_user_meta_data->>'picture',
                            NULL
                        ) as avatar_url,
                        CASE WHEN u.email LIKE '%admin%' THEN 'admin' ELSE 'player' END as role,
                        1,
                        0,
                        u.created_at,
                        u.created_at
                    FROM auth.users u
                    ON CONFLICT (id) DO UPDATE
                    SET 
                        email = EXCLUDED.email,
                        phone = EXCLUDED.phone,
                        display_name = EXCLUDED.display_name,
                        avatar_url = EXCLUDED.avatar_url;

                    INSERT INTO public.wallets (user_id, gold_balance, gem_balance, version, updated_at)
                    SELECT id, 0, 0, 1, timezone('utc'::text, now())
                    FROM public.profiles
                    ON CONFLICT (user_id) DO NOTHING;
                ";
                await context.Database.ExecuteSqlRawAsync(syncRealUsersSql);
                result.ExecutedSteps.Add("Đã tự động đồng bộ tài khoản thực tế từ auth.users sang public.profiles.");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Lỗi đồng bộ tài khoản auth.users: {ex.Message}");
            }

            // 6. Seed Admin Logs mẫu nếu trống
            try
            {
                var logCount = await context.AdminLogs.CountAsync();
                if (logCount == 0)
                {
                    context.AdminLogs.Add(new AdminLog
                    {
                        AdminUsername = "Admin_System",
                        ActionName = "Khởi tạo hệ thống",
                        ActionDetails = "Hệ thống Sử Đại Việt khởi động thành công, cơ sở dữ liệu đã sẵn sàng.",
                        CreatedAt = DateTime.UtcNow
                    });
                    await context.SaveChangesAsync();
                    result.ExecutedSteps.Add("Đã tạo log hệ thống khởi đầu.");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Lỗi seed Admin Logs: {ex.Message}");
            }

            result.Success = result.Errors.Count == 0;
            result.Message = result.Success ? "Đồng bộ Database thành công 100%!" : "Đồng bộ hoàn tất với một số cảnh báo.";
            return result;
        }
    }
}
