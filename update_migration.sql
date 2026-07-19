-- =====================================================================
-- SQL MIGRATION: UPDATE PLAYER_BROADCAST_CLAIMS SCHEMA & FUNCTIONS
-- Chạy trực tiếp trên Supabase SQL Editor để cập nhật database hiện tại
-- =====================================================================

-- 1. Thêm cột is_read và is_claimed vào bảng player_broadcast_claims nếu chưa có
ALTER TABLE public.player_broadcast_claims 
ADD COLUMN IF NOT EXISTS is_read BOOLEAN DEFAULT true NOT NULL;

ALTER TABLE public.player_broadcast_claims 
ADD COLUMN IF NOT EXISTS is_claimed BOOLEAN DEFAULT false NOT NULL;

-- 2. Cập nhật lại Stored Procedure claim_mailbox_attachments hỗ trợ cột mới
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
    -- 1. Khóa dòng thư để claim an toàn
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

    -- 2. Kiểm tra quyền sở hữu & đã nhận quà chưa
    IF v_receiver_id IS NOT NULL THEN
        IF v_receiver_id != p_user_id THEN
            RETURN 'ERROR: Thư này không thuộc sở hữu của bạn.';
        END IF;
        IF v_is_claimed THEN
            RETURN 'ERROR: Quà đã được nhận trước đó.';
        END IF;
        -- Cập nhật trạng thái nhận quà thư cá nhân
        UPDATE public.mailbox SET is_claimed = true, is_read = true, updated_at = now() WHERE id = p_mail_id;
    ELSE
        -- Thư Broadcast: Chèn dòng claims nguyên tử chống nhận trùng (khi nhận quà nghĩa là đã đọc và đã nhận)
        INSERT INTO public.player_broadcast_claims (user_id, mail_id, is_read, is_claimed, claimed_at)
        VALUES (p_user_id, p_mail_id, true, true, now())
        ON CONFLICT (user_id, mail_id) DO NOTHING;
        
        GET DIAGNOSTICS v_affected = ROW_COUNT;
        IF v_affected = 0 THEN
            RETURN 'ERROR: Nghĩa sĩ đã nhận quà từ thư broadcast này rồi.';
        END IF;
    END IF;

    -- Khóa ví người chơi để cộng tiền an toàn
    PERFORM 1 FROM public.wallets WHERE user_id = p_user_id FOR UPDATE;

    -- 3. Phân tích quà tặng đính kèm từ JSON
    IF v_attachments ? 'gold' THEN
        v_gold_reward := (v_attachments->>'gold')::INT;
    END If;
    IF v_attachments ? 'gems' THEN
        v_gem_reward := (v_attachments->>'gems')::INT;
    END IF;

    -- Cộng vàng/ngọc
    IF v_gold_reward > 0 OR v_gem_reward > 0 THEN
        UPDATE public.wallets
        SET gold_balance = gold_balance + v_gold_reward,
            gem_balance = gem_balance + v_gem_reward,
            version = version + 1,
            updated_at = now()
        WHERE user_id = p_user_id;

        -- Ghi log tài chính cho giao dịch nhận thưởng hòm thư công khai/cá nhân
        INSERT INTO public.transactions (user_id, transaction_type, amount_gold, amount_gem, reference_id, status, created_at, updated_at)
        VALUES (p_user_id, 'MailboxClaim', v_gold_reward, v_gem_reward, 'MAIL-' || p_mail_id, 'Completed', now(), now());
    END IF;

    -- Cộng vật phẩm nếu có
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
