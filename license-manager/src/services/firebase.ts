import { initializeApp, getApps, getApp } from "firebase/app";
import { getAuth } from "firebase/auth";
import { getFirestore } from "firebase/firestore";
import { getFunctions } from "firebase/functions";

export const firebaseConfig = {
  apiKey: "AIzaSyDkdYJIAcmAPyhxIs2DoJJH9e7q0WTfalI",
  authDomain: "nexus-ls.firebaseapp.com",
  projectId: "nexus-ls",
  storageBucket: "nexus-ls.firebasestorage.app",
  messagingSenderId: "376370391648",
  appId: "1:376370391648:web:e4dfc7cdcff70e38f73105",
  measurementId: "G-VH1PQWR7HD"
};

const app = !getApps().length ? initializeApp(firebaseConfig) : getApp();

export const auth = getAuth(app);
export const db = getFirestore(app);
export const functions = getFunctions(app, "southamerica-east1");

export default app;
