-- Stellt für zwei Profile genau einen privaten Raum bereit.
-- Der Storage Service ruft diese Funktion mit seinem Backend-Schlüssel auf.
CREATE OR REPLACE FUNCTION public.get_or_create_private_room(
    p_sender_id uuid,
    p_target_id uuid
)
RETURNS TABLE(room_id uuid)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $function$
DECLARE
    private_room_id uuid;
BEGIN
    IF p_sender_id IS NULL OR p_target_id IS NULL THEN
        RAISE EXCEPTION 'Sender und Empfänger dürfen nicht leer sein.';
    END IF;

    IF p_sender_id = p_target_id THEN
        RAISE EXCEPTION 'Sender und Empfänger müssen unterschiedlich sein.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.profiles WHERE id = p_sender_id) THEN
        RAISE EXCEPTION 'Senderprofil wurde nicht gefunden.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.profiles WHERE id = p_target_id) THEN
        RAISE EXCEPTION 'Empfängerprofil wurde nicht gefunden.';
    END IF;

    -- A -> B und B -> A erhalten denselben Lock. Dadurch können parallele
    -- erste Nachrichten nicht zwei Räume für dasselbe Benutzerpaar erzeugen.
    PERFORM pg_advisory_xact_lock(
        hashtextextended(
            least(p_sender_id::text, p_target_id::text) || ':' ||
            greatest(p_sender_id::text, p_target_id::text),
            0
        )
    );

    SELECT room.id
    INTO private_room_id
    FROM public.rooms AS room
    WHERE room.is_group = false
      AND EXISTS (
          SELECT 1
          FROM public.room_members AS member
          WHERE member.room_id = room.id
            AND member.user_id = p_sender_id
      )
      AND EXISTS (
          SELECT 1
          FROM public.room_members AS member
          WHERE member.room_id = room.id
            AND member.user_id = p_target_id
      )
      AND (
          SELECT count(*)
          FROM public.room_members AS member
          WHERE member.room_id = room.id
      ) = 2
    ORDER BY room.created_at, room.id
    LIMIT 1;

    IF private_room_id IS NULL THEN
        INSERT INTO public.rooms (name, is_group, created_by)
        VALUES ('Privater Chat', false, p_sender_id)
        RETURNING id INTO private_room_id;

        INSERT INTO public.room_members (room_id, user_id)
        VALUES
            (private_room_id, p_sender_id),
            (private_room_id, p_target_id);
    END IF;

    RETURN QUERY SELECT private_room_id;
END
$function$;

REVOKE ALL ON FUNCTION public.get_or_create_private_room(uuid, uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.get_or_create_private_room(uuid, uuid) FROM anon, authenticated;
GRANT EXECUTE ON FUNCTION public.get_or_create_private_room(uuid, uuid) TO service_role;

NOTIFY pgrst, 'reload schema';
