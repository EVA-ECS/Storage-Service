-- Erst NACH Veröffentlichung des aktualisierten Storage-Service ausführen.
-- NOT VALID lässt vorhandene NULL-Altbestände zu, wird aber für neue/geänderte Zeilen erzwungen.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';

DO $migration$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema = 'public' AND table_name = 'messages'
                     AND column_name = 'receiver_id' AND data_type = 'uuid') THEN
        RAISE EXCEPTION 'messages.receiver_id uuid fehlt. Zuerst 20260905_add_receiver_id.sql ausführen.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conrelid = 'public.messages'::regclass AND conname = 'messages_receiver_id_required') THEN
        ALTER TABLE public.messages ADD CONSTRAINT messages_receiver_id_required
            CHECK (receiver_id IS NOT NULL) NOT VALID;
    END IF;
END
$migration$;

COMMIT;
