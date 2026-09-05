-- Optionaler, absichtlich zunächst rückgängig gemachter Probelauf für alte private Nachrichten.
-- Vorher Migration ausführen und bestätigen: Die Mitglieder der freigegebenen Räume
-- waren auch beim Versand genau diese beiden Benutzer. Aktuelle Mitgliedschaft allein
-- beweist keinen historischen Empfänger. Keine Gruppen oder mehrdeutigen Räume freigeben.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';

CREATE TEMP TABLE receiver_backfill_approved_rooms (room_id uuid PRIMARY KEY) ON COMMIT DROP;
-- Hier nur geprüfte Raum-IDs eintragen; ohne Einträge wird nichts geändert:
-- INSERT INTO receiver_backfill_approved_rooms VALUES ('GEPRUEFTE-RAUM-UUID');

WITH candidates AS (
    SELECT m.id, recipient.user_id AS receiver_id
    FROM public.messages m
    JOIN receiver_backfill_approved_rooms approved ON approved.room_id = m.room_id
    JOIN public.rooms r ON r.id = m.room_id AND r.is_group = false
    JOIN public.room_members sender ON sender.room_id = m.room_id AND sender.user_id = m.sender_id
    JOIN public.room_members recipient ON recipient.room_id = m.room_id AND recipient.user_id <> m.sender_id
    WHERE m.receiver_id IS NULL
      AND (SELECT count(*) FROM public.room_members member WHERE member.room_id = m.room_id) = 2
)
UPDATE public.messages m SET receiver_id = candidates.receiver_id
FROM candidates
WHERE m.id = candidates.id AND m.receiver_id IS NULL
RETURNING m.id, m.room_id, m.sender_id, m.receiver_id;

-- Erst nach Prüfung und Abstimmung mit dem Team durch COMMIT ersetzen.
ROLLBACK;
