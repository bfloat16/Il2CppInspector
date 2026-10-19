        for section in ("genshinEmptyArrays", "genshinNativeStrings"):
            entries = metadata.get(section, [])
            if entries:
                self._status.update_step("Processing MORAX runtime caches", len(entries))
            for d in entries:
                self.define_field_from_json(d)
                self._status.update_progress()
