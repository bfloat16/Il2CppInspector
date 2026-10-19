        caches = metadata.get("bh3RuntimeCaches", [])
        if caches:
            self._status.update_step("Processing MORAX runtime caches", len(caches))
        for d in caches:
            self.define_field_from_json(d)
            self._status.update_progress()
