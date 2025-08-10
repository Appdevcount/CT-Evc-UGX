import { createStore, applyMiddleware } from "redux";
import { rootReducer } from "../reducers/index";
import thunk, { ThunkMiddleware } from "redux-thunk";
import { createLogger } from "redux-logger";

const logger = createLogger();

export type AppState = ReturnType<typeof rootReducer>;

export const ucxReduxStore = createStore(
  rootReducer,
  applyMiddleware(thunk as ThunkMiddleware<AppState>, logger)
);
